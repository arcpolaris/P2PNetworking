if (-not (Get-Command dotnet-suggest -ErrorAction SilentlyContinue)) {
    dotnet tool install -g dotnet-suggest | Out-Null
}

Register-ArgumentCompleter -Native -CommandName pchat.ps1,.\pchat.ps1,Chatroom.exe -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    dotnet-suggest get --position $cursorPosition -- "$commandAst" |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}

& "$PSScriptRoot\Chatroom.exe" @args
exit $LASTEXITCODE