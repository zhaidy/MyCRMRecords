<#
.SYNOPSIS
    Starts SQL Server LocalDB for the database tests (pr-checks.yml and publish.yml) and checks that it answers. The
    workflows already start it in the background at the beginning of the job, so here it is usually running or close.
.DESCRIPTION
    On a fresh GitHub runner LocalDB sometimes fails to start ("SQL Server process failed to start"), and every
    SQL Server test then fails. This connects once LocalDB is started; when it does not answer, it recreates the
    instance and tries again, three times in all, before failing the step with LocalDB's own message.
    Once LocalDB answers, MYCRMRECORDS_TEST_SQLSERVER is set for the following steps. Without LocalDB on the runner,
    the SQL Server tests are skipped.
#>
if (-not (Get-Command SqlLocalDB -ErrorAction SilentlyContinue)) {
    Write-Host 'SQL Server LocalDB is not installed on this runner; the SQL Server tests will be skipped.'
    return
}

$connectionString = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true'
$lastError = ''
for ($attempt = 1; $attempt -le 3; $attempt++) {
    if ($attempt -gt 1) {
        Write-Host "LocalDB did not answer; recreating the instance (attempt $attempt of 3)."
        # SqlLocalDB writes its errors to the output; the next connection tells whether the instance works.
        SqlLocalDB stop MSSQLLocalDB -k | Out-Host
        SqlLocalDB delete MSSQLLocalDB | Out-Host
        SqlLocalDB create MSSQLLocalDB | Out-Host
    }
    SqlLocalDB start MSSQLLocalDB | Out-Host
    $connection = New-Object System.Data.SqlClient.SqlConnection ($connectionString + ';Connect Timeout=60')
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT 1'
        $null = $command.ExecuteScalar()
        # AppendAllText writes UTF-8 without a byte order mark, which GITHUB_ENV needs.
        [IO.File]::AppendAllText($env:GITHUB_ENV, "MYCRMRECORDS_TEST_SQLSERVER=$connectionString`n")
        Write-Host 'LocalDB answers.'
        return
    } catch {
        $lastError = $_.Exception.Message
        Write-Host ('LocalDB did not answer: ' + $lastError)
        Start-Sleep -Seconds 5
    } finally {
        $connection.Dispose()
    }
}
throw ('SQL Server LocalDB did not start after 3 attempts: ' + $lastError)
