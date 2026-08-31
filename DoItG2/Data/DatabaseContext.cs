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
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD FOB VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'CIF')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD CIF VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'NETTO')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD NETTO VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'BRUTO')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD BRUTO VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'ASURANSI')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD ASURANSI VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'FREIGHT')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD FREIGHT VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'NDPBM')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD NDPBM VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'KD_VAL')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD KD_VAL VARCHAR(20) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'STATUS')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD STATUS VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'APPROVAL_STATUS')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD APPROVAL_STATUS VARCHAR(50) DEFAULT 'DRAFT';
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'REVIEW_NOTES')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD REVIEW_NOTES VARCHAR(1000) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'SUBMITTED_BY')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD SUBMITTED_BY VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'SUBMITTED_DATE')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD SUBMITTED_DATE DATETIME NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'APPROVED_BY')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD APPROVED_BY VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'APPROVED_DATE')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD APPROVED_DATE DATETIME NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'TOTAL_BM')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD TOTAL_BM DECIMAL(18,2) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'TOTAL_PPN')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD TOTAL_PPN DECIMAL(18,2) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'TOTAL_PPH')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD TOTAL_PPH DECIMAL(18,2) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'TOTAL_PUNGUTAN')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD TOTAL_PUNGUTAN DECIMAL(18,2) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'NILAI_PABEAN')
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD NILAI_PABEAN DECIMAL(18,2) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER') AND name = 'ENTITY')
                        BEGIN
                            ALTER TABLE PIB_DOIT_FINAL_HEADER ADD ENTITY VARCHAR(10) DEFAULT 'SIM';
                            EXEC('UPDATE PIB_DOIT_FINAL_HEADER SET ENTITY = CASE WHEN NM_IMO LIKE ''%SALES%'' OR ID_IMP LIKE ''%011297389%'' OR ID_IMP LIKE ''%01.129.738%'' THEN ''SIS'' ELSE ''SIM'' END WHERE ENTITY IS NULL');
                        END
                    END

                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_HEADER')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'ENTITY')
                        BEGIN
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD ENTITY VARCHAR(10) DEFAULT 'SIM';
                            EXEC('UPDATE PEB_DOIT_FINAL_HEADER SET ENTITY = CASE WHEN NAMAEKS LIKE ''%SALES%'' OR NPWPEKS LIKE ''%011297389%'' OR NPWPEKS LIKE ''%01.129.738%'' THEN ''SIS'' ELSE ''SIM'' END WHERE ENTITY IS NULL');
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'NAMABELI')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD NAMABELI VARCHAR(100) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'ALMTBELI')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD ALMTBELI VARCHAR(200) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'NEGBELI')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD NEGBELI VARCHAR(100) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'NOPEN')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD NOPEN VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'TGL_NOPEN')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD TGL_NOPEN DATETIME NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'NONPE')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD NONPE VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'TGL_NPE')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD TGL_NPE DATETIME NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'APPROVAL_STATUS')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD APPROVAL_STATUS VARCHAR(50) DEFAULT 'DRAFT';
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'REVIEW_NOTES')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD REVIEW_NOTES VARCHAR(1000) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'SUBMITTED_BY')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD SUBMITTED_BY VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'SUBMITTED_DATE')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD SUBMITTED_DATE DATETIME NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'APPROVED_BY')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD APPROVED_BY VARCHAR(50) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER') AND name = 'APPROVED_DATE')
                            ALTER TABLE PEB_DOIT_FINAL_HEADER ADD APPROVED_DATE DATETIME NULL;
                    END

                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_DETAIL')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_DETAIL') AND name = 'URBRG')
                            ALTER TABLE PEB_DOIT_FINAL_DETAIL ADD URBRG VARCHAR(500) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_DETAIL') AND name = 'JMLSAT')
                            ALTER TABLE PEB_DOIT_FINAL_DETAIL ADD JMLSAT DECIMAL(18,4) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_DETAIL') AND name = 'KDSAT')
                            ALTER TABLE PEB_DOIT_FINAL_DETAIL ADD KDSAT VARCHAR(20) DEFAULT 'PCE';
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_DETAIL') AND name = 'NETTODET')
                            ALTER TABLE PEB_DOIT_FINAL_DETAIL ADD NETTODET DECIMAL(18,4) DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PEB_DOIT_FINAL_DETAIL') AND name = 'FOBDET')
                            ALTER TABLE PEB_DOIT_FINAL_DETAIL ADD FOBDET DECIMAL(18,4) DEFAULT 0;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_CONTAINER')
                    BEGIN
                        CREATE TABLE PEB_DOIT_FINAL_CONTAINER (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            CAR VARCHAR(100) NOT NULL,
                            NOCONT VARCHAR(50),
                            UKURCONT VARCHAR(20),
                            TIPECONT VARCHAR(20)
                        );
                        CREATE INDEX IX_peb_cont_car ON PEB_DOIT_FINAL_CONTAINER (CAR);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_DOCUMENT')
                    BEGIN
                        CREATE TABLE PEB_DOIT_FINAL_DOCUMENT (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            CAR VARCHAR(100) NOT NULL,
                            SERI INT,
                            KDDOK VARCHAR(50),
                            NODOK VARCHAR(100),
                            TGDOK DATE
                        );
                        CREATE INDEX IX_peb_doc_car ON PEB_DOIT_FINAL_DOCUMENT (CAR);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_RESPON')
                    BEGIN
                        CREATE TABLE PEB_DOIT_FINAL_RESPON (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            CAR VARCHAR(100) NOT NULL,
                            RESKD VARCHAR(10),
                            RESTG DATETIME,
                            NOPEN VARCHAR(50),
                            TGPEN DATETIME,
                            DESKRIPSI VARCHAR(500),
                            DIBACA BIT DEFAULT 0
                        );
                        CREATE INDEX IX_peb_respon_car ON PEB_DOIT_FINAL_RESPON (CAR);
                    END

                    -- Performance Optimization Indexes
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PIB_DOIT_FINAL_HEADER')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PIB_CAR' AND object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER'))
                            CREATE NONCLUSTERED INDEX IX_PIB_CAR ON PIB_DOIT_FINAL_HEADER (CAR);
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PIB_ENTITY_STATUS' AND object_id = OBJECT_ID('PIB_DOIT_FINAL_HEADER'))
                            CREATE NONCLUSTERED INDEX IX_PIB_ENTITY_STATUS ON PIB_DOIT_FINAL_HEADER (ENTITY, APPROVAL_STATUS, CREATION_DATE DESC);
                    END

                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PEB_DOIT_FINAL_HEADER')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PEB_CAR' AND object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER'))
                            CREATE NONCLUSTERED INDEX IX_PEB_CAR ON PEB_DOIT_FINAL_HEADER (CAR);
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PEB_ENTITY_STATUS' AND object_id = OBJECT_ID('PEB_DOIT_FINAL_HEADER'))
                            CREATE NONCLUSTERED INDEX IX_PEB_ENTITY_STATUS ON PEB_DOIT_FINAL_HEADER (ENTITY, APPROVAL_STATUS, CREATED_DATE DESC);
                    END

                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_APPROVAL_LOG')
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DOIT_APPROVAL_LOG_CAR' AND object_id = OBJECT_ID('DOIT_APPROVAL_LOG'))
                            CREATE NONCLUSTERED INDEX IX_DOIT_APPROVAL_LOG_CAR ON DOIT_APPROVAL_LOG (CAR, ACTION_DATE DESC);
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
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_KURS_PAJAK')
                    BEGIN
                        CREATE TABLE DOIT_KURS_PAJAK (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            KD_VAL VARCHAR(10) NOT NULL,
                            NM_VAL VARCHAR(100) NOT NULL,
                            NILAI_NDPBM DECIMAL(18,4) NOT NULL,
                            TGL_AWAL DATE NOT NULL,
                            TGL_AKHIR DATE NOT NULL,
                            NO_KMK VARCHAR(100) DEFAULT 'KMK/2026/WEEKLY',
                            IS_ACTIVE BIT NOT NULL DEFAULT 1,
                            CREATED_AT DATETIME DEFAULT GETDATE(),
                            UPDATED_AT DATETIME DEFAULT GETDATE()
                        );

                        INSERT INTO DOIT_KURS_PAJAK (KD_VAL, NM_VAL, NILAI_NDPBM, TGL_AWAL, TGL_AKHIR, NO_KMK)
                        VALUES 
                        ('USD', 'US Dollar', 16250.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026'),
                        ('JPY', 'Japanese Yen (100)', 10650.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026'),
                        ('EUR', 'Euro', 17480.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026'),
                        ('SGD', 'Singapore Dollar', 12150.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026'),
                        ('CNY', 'Chinese Yuan', 2240.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026'),
                        ('THB', 'Thai Baht', 450.0000, CAST(GETDATE() AS DATE), DATEADD(DAY, 7, CAST(GETDATE() AS DATE)), 'KMK-38/MK.10/2026');
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_NOTIFIKASI')
                    BEGIN
                        CREATE TABLE DOIT_NOTIFIKASI (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            USER_NAME VARCHAR(50) NULL,
                            TITLE VARCHAR(200) NOT NULL,
                            MESSAGE VARCHAR(1000) NOT NULL,
                            TYPE VARCHAR(20) DEFAULT 'INFO', -- INFO, SUCCESS, WARNING, DANGER
                            LINK_URL VARCHAR(255) NULL,
                            IS_READ BIT NOT NULL DEFAULT 0,
                            CREATED_AT DATETIME DEFAULT GETDATE()
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_APPROVAL_LOG')
                    BEGIN
                        CREATE TABLE DOIT_APPROVAL_LOG (
                            ID INT IDENTITY(1,1) PRIMARY KEY,
                            CAR VARCHAR(50) NOT NULL,
                            DOKUMEN_TYPE VARCHAR(20) NOT NULL, -- PIB, PEB
                            PREV_STATUS VARCHAR(50),
                            NEW_STATUS VARCHAR(50) NOT NULL,
                            ACTION VARCHAR(50) NOT NULL, -- SUBMIT, APPROVE, REJECT, TRANSMIT
                            NOTES VARCHAR(1000),
                            ACTION_BY VARCHAR(50) NOT NULL,
                            ACTION_DATE DATETIME DEFAULT GETDATE()
                        );
                    END
                    
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DOIT_DOCUMENT_LOCK')
                    BEGIN
                        CREATE TABLE DOIT_DOCUMENT_LOCK (
                            CAR VARCHAR(50) NOT NULL PRIMARY KEY,
                            DOC_TYPE VARCHAR(20) NOT NULL, -- PIB, PEB
                            LOCKED_BY_USER VARCHAR(50) NOT NULL,
                            LOCKED_BY_NAME VARCHAR(100) NOT NULL,
                            LOCKED_BY_ENTITY VARCHAR(20) NOT NULL, -- SIM, SIS
                            LOCKED_AT DATETIME NOT NULL DEFAULT GETDATE(),
                            EXPIRES_AT DATETIME NOT NULL,
                            LAST_HEARTBEAT DATETIME NOT NULL DEFAULT GETDATE()
                        );
                        CREATE INDEX IX_doc_lock_expires ON DOIT_DOCUMENT_LOCK (EXPIRES_AT);
                    END";
                using (var masterCmd = new SqlCommand(createMasterTablesSql, dbConn))
                {
                    await masterCmd.ExecuteNonQueryAsync();
                }

                // Step 1: Ensure entity_access column exists in a dedicated command batch
                var ensureColumnSql = @"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('doit_user') AND name = 'entity_access')
                    BEGIN
                        ALTER TABLE doit_user ADD entity_access VARCHAR(20) NOT NULL CONSTRAINT DF_doit_user_entity_access DEFAULT 'ALL';
                    END";
                using (var colCmd = new SqlCommand(ensureColumnSql, dbConn))
                {
                    await colCmd.ExecuteNonQueryAsync();
                }

                // Step 2: Update legacy user roles to standard 3 roles and seed default users
                var updateRolesSql = @"
                    UPDATE doit_user SET user_type = 'STAFF_EXIM' WHERE user_type IN ('STAFF', 'VIEWER', 'KITE');
                    UPDATE doit_user SET user_type = 'ADMIN_DOKUMEN' WHERE user_type = 'ADMIN';
                    UPDATE doit_user SET user_type = 'MANAJER_OPS' WHERE user_type IN ('SUPERVISOR', 'MANAGER');

                    -- Update existing default users if present
                    UPDATE doit_user SET entity_access = 'ALL', pib_sim = 1, pib_sis = 1, peb_sim = 1, peb_sis = 1 WHERE user_name IN ('admin', 'rizki');
                    UPDATE doit_user SET entity_access = 'SIM', pib_sim = 1, pib_sis = 0, peb_sim = 1, peb_sis = 0 WHERE user_name = 'dinda';
                    UPDATE doit_user SET entity_access = 'SIS', pib_sim = 0, pib_sis = 1, peb_sim = 0, peb_sis = 1 WHERE user_name = 'heru';

                    -- 1. user_sim (Hanya bisa masuk SIM)
                    IF NOT EXISTS (SELECT 1 FROM doit_user WHERE user_name = 'user_sim')
                    BEGIN
                        INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, entity_access,
                            is_admin, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb,
                            pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84, peb_authorize_81, peb_authorize_84, created_date)
                        VALUES ('user_sim', 'Operator SIM (Indomobil Motor)', 'user_sim@suzuki.co.id',
                            '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6', 'STAFF_EXIM', 1, 'SIM',
                            0, 1, 1, 0, 1, 1,
                            1, 0, 1, 0, 0, 0, 0, 0, GETDATE());
                    END
                    ELSE
                    BEGIN
                        UPDATE doit_user SET entity_access = 'SIM', pib_sim = 1, pib_sis = 0, peb_sim = 1, peb_sis = 0 WHERE user_name = 'user_sim';
                    END

                    -- 2. user_sis (Hanya bisa masuk SIS)
                    IF NOT EXISTS (SELECT 1 FROM doit_user WHERE user_name = 'user_sis')
                    BEGIN
                        INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, entity_access,
                            is_admin, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb,
                            pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84, peb_authorize_81, peb_authorize_84, created_date)
                        VALUES ('user_sis', 'Operator SIS (Indomobil Sales)', 'user_sis@suzuki.co.id',
                            '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6', 'STAFF_EXIM', 1, 'SIS',
                            0, 1, 1, 0, 1, 1,
                            0, 1, 0, 1, 0, 0, 0, 0, GETDATE());
                    END
                    ELSE
                    BEGIN
                        UPDATE doit_user SET entity_access = 'SIS', pib_sim = 0, pib_sis = 1, peb_sim = 0, peb_sis = 1 WHERE user_name = 'user_sis';
                    END

                    -- 3. user_dual (Bisa masuk keduanya: SIM & SIS)
                    IF NOT EXISTS (SELECT 1 FROM doit_user WHERE user_name = 'user_dual')
                    BEGIN
                        INSERT INTO doit_user (user_name, full_name, email, password_hash, user_type, is_active, entity_access,
                            is_admin, is_partmaster, is_pi, is_matrix, is_fasilitas, is_pkb,
                            pib_sim, pib_sis, peb_sim, peb_sis, pib_authorize_81, pib_authorize_84, peb_authorize_81, peb_authorize_84, created_date)
                        VALUES ('user_dual', 'Koordinator Dual Access (SIM & SIS)', 'user_dual@suzuki.co.id',
                            '$2a$11$/zNH2SxjnRdqxt1BUK7fyus1LWXqp3RDBjtUWRiRn/17PAqApOhn6', 'ADMIN_DOKUMEN', 1, 'ALL',
                            1, 1, 1, 1, 1, 1,
                            1, 1, 1, 1, 1, 1, 1, 1, GETDATE());
                    END
                    ELSE
                    BEGIN
                        UPDATE doit_user SET entity_access = 'ALL', pib_sim = 1, pib_sis = 1, peb_sim = 1, peb_sis = 1 WHERE user_name = 'user_dual';
                    END
                ";
                using (var roleCmd = new SqlCommand(updateRolesSql, dbConn))
                {
                    await roleCmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing EnsureDatabaseCreatedAsync");
        }
    }
}

