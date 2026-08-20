using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace DoItG2.Data;

public class DatabaseContext
{
    private readonly string _connectionString;
    private readonly string _oracleConnectionString;
    private readonly ILogger<DatabaseContext> _logger;

    public DatabaseContext(IConfiguration configuration, ILogger<DatabaseContext> logger)
    {   
        _connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("SQL Server connection string not configured.");
        _oracleConnectionString = configuration.GetConnectionString("OracleSilo") ?? "";
        _logger = logger;
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public IDbConnection CreateOracleConnection() => new Oracle.ManagedDataAccess.Client.OracleConnection(_oracleConnectionString);

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        try
        {
            return await conn.QueryAsync<T>(sql, param);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query error: {Sql}", sql);
            throw;
        }
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        try
        {
            return await conn.ExecuteAsync(sql, param);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute error: {Sql}", sql);
            throw;
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<T>(sql, param);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            var databaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "DO_IT_G2" : builder.InitialCatalog;

            builder.InitialCatalog = "master";
            using (var masterConn = new SqlConnection(builder.ConnectionString))
            {
                await masterConn.OpenAsync();
                var checkDbSql = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}') CREATE DATABASE [{databaseName}];";
                using var cmd = new SqlCommand(checkDbSql, masterConn);
                await cmd.ExecuteNonQueryAsync();
            }

            using (var dbConn = new SqlConnection(_connectionString))
            {
                await dbConn.OpenAsync();
                var checkTableSql = "SELECT COUNT(*) FROM sys.tables WHERE name = 'doit_user'";
                using var cmd = new SqlCommand(checkTableSql, dbConn);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (count == 0)
                {
                    _logger.LogInformation("Database {DatabaseName} is empty. Initializing schema...", databaseName);

                    var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), "database_schema.sql");
                    if (!File.Exists(schemaPath))
                    {
                        schemaPath = Path.Combine(AppContext.BaseDirectory, "database_schema.sql");
                    }

                    if (File.Exists(schemaPath))
                    {
                        var sqlContent = await File.ReadAllTextAsync(schemaPath);
                        var batches = System.Text.RegularExpressions.Regex.Split(
                            sqlContent,
                            @"^\s*GO\s*$",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline
                        );

                        foreach (var batch in batches)
                        {
                            var trimmed = batch.Trim();
                            if (!string.IsNullOrWhiteSpace(trimmed))
                            {
                                try
                                {
                                    using var batchCmd = new SqlCommand(trimmed, dbConn);
                                    await batchCmd.ExecuteNonQueryAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Batch execution note");
                                }
                            }
                        }
                        _logger.LogInformation("Database schema initialized successfully.");
                    }
                }

                var alterColumnsSql = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PIB_DOIT_FINAL_HEADER')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'FOB')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD FOB VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'CIF')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD CIF VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'NETTO')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD NETTO VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'BRUTO')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD BRUTO VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'ASURANSI')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD ASURANSI VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'FREIGHT')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD FREIGHT VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'NDPBM')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD NDPBM VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'KD_VAL')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD KD_VAL VARCHAR(10) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'STATUS')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD STATUS VARCHAR(20) NULL;
                    END";
                using (var alterCmd = new SqlCommand(alterColumnsSql, dbConn))
                {
                    await alterCmd.ExecuteNonQueryAsync();
                }

                var createMasterTablesSql = @"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_MASTER_PEMASOK')
                    BEGIN
                        CREATE TABLE DOIT_MASTER_PEMASOK (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            KD_PEMASOK VARCHAR(20) NOT NULL UNIQUE,
                            NM_PEMASOK VARCHAR(200) NOT NULL,
                            ALM_PEMASOK VARCHAR(300),
                            NEG_PEMASOK VARCHAR(5) DEFAULT 'JP',
                            IS_ACTIVE BIT NOT NULL DEFAULT 1
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_MASTER_PEMBELI')
                    BEGIN
                        CREATE TABLE DOIT_MASTER_PEMBELI (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            KD_PEMBELI VARCHAR(20) NOT NULL UNIQUE,
                            NM_PEMBELI VARCHAR(200) NOT NULL,
                            ALM_PEMBELI VARCHAR(300),
                            NEG_PEMBELI VARCHAR(5) DEFAULT 'JP',
                            IS_ACTIVE BIT NOT NULL DEFAULT 1
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_MASTER_FASILITAS')
                    BEGIN
                        CREATE TABLE DOIT_MASTER_FASILITAS (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            NO_SKEP VARCHAR(100) NOT NULL UNIQUE,
                            TGL_SKEP DATE,
                            JENIS_FASILITAS VARCHAR(50) DEFAULT 'KITE',
                            DESKRIPSI VARCHAR(300),
                            IS_ACTIVE BIT NOT NULL DEFAULT 1
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_MASTER_LARTAS')
                    BEGIN
                        CREATE TABLE DOIT_MASTER_LARTAS (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            NO_PI VARCHAR(100) NOT NULL UNIQUE,
                            KOMODITAS VARCHAR(200) NOT NULL,
                            KUOTA_AWAL DECIMAL(18,2) NOT NULL DEFAULT 0,
                            KUOTA_TERPAKAI DECIMAL(18,2) NOT NULL DEFAULT 0,
                            SATUAN VARCHAR(10) DEFAULT 'KG',
                            TGL_BERLAKU DATE,
                            IS_ACTIVE BIT NOT NULL DEFAULT 1
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_MASTER_PKB')
                    BEGIN
                        CREATE TABLE DOIT_MASTER_PKB (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            PIB_TYPE VARCHAR(10) DEFAULT '81',
                            CAR VARCHAR(50) NOT NULL,
                            FASILITAS VARCHAR(100),
                            GUDANG VARCHAR(100),
                            PETUGAS VARCHAR(100),
                            NOPHONE VARCHAR(50),
                            ALMTSIAP VARCHAR(300),
                            IS_ACTIVE BIT NOT NULL DEFAULT 1
                        );
                    END";
                using (var masterCmd = new SqlCommand(createMasterTablesSql, dbConn))
                {
                    await masterCmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing EnsureDatabaseCreatedAsync");
        }
    }
}

