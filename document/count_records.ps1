$connectionString = "Server=(localdb)\MSSQLLocalDB;Database=DO_IT_G2;Integrated Security=True;TrustServerCertificate=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$conn.Open()

$tables = @("PIB_DOIT_FINAL_HEADER", "PEB_DOIT_FINAL_HEADER", "DOIT_MASTER_PART", "doit_audit_log")
foreach ($t in $tables) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM $t"
    $count = $cmd.ExecuteScalar()
    Write-Host "${t}: ${count} data"
}
$conn.Close()
