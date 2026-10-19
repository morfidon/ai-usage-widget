function Get-WidgetDataDirectory([string]$Root) {
    $layout = Join-Path $Root 'install-layout.json'
    if (Test-Path -LiteralPath $layout) {
        $value = (Get-Content -LiteralPath $layout -Raw | ConvertFrom-Json).DataDirectory
        if (-not $value -or -not [IO.Path]::IsPathRooted($value)) { throw 'Invalid installation layout.' }
        return [IO.Path]::GetFullPath($value)
    }
    return Join-Path $Root 'data'
}
