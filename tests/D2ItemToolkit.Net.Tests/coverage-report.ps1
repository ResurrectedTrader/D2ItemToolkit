# Reports line/branch coverage per class and lists every uncovered line and
# partially-covered branch point in ItemDescription.cs.
$rep = Get-ChildItem -Recurse -Filter coverage.cobertura.xml "$PSScriptRoot\TestResults" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($null -eq $rep) { throw "no coverage report found; run dotnet test --collect:'XPlat Code Coverage'" }

$xml = [xml](Get-Content $rep.FullName)

"=== per class ==="
foreach ($pkg in $xml.coverage.packages.package) {
    foreach ($c in $pkg.classes.class) {
        "{0,-46} lines={1,7:P2} branches={2,7:P2}" -f `
            $c.name, [double]$c.'line-rate', [double]$c.'branch-rate'
    }
}

"`n=== uncovered lines ==="
$any = $false
foreach ($pkg in $xml.coverage.packages.package) {
    foreach ($c in $pkg.classes.class) {
        foreach ($l in $c.lines.line) {
            if ([int]$l.hits -eq 0) {
                "{0}:{1}" -f (Split-Path $c.filename -Leaf), $l.number
                $any = $true
            }
        }
    }
}
if (-not $any) { "none" }

"`n=== partially covered branch points ==="
$any = $false
foreach ($pkg in $xml.coverage.packages.package) {
    foreach ($c in $pkg.classes.class) {
        foreach ($l in $c.lines.line) {
            if ($l.branch -eq 'True' -and $l.'condition-coverage' -notmatch '^100%') {
                "{0}:{1} {2}" -f (Split-Path $c.filename -Leaf), $l.number, $l.'condition-coverage'
                $any = $true
            }
        }
    }
}
if (-not $any) { "none" }

"`n=== totals ==="
"lines={0:P2} branches={1:P2}" -f [double]$xml.coverage.'line-rate', [double]$xml.coverage.'branch-rate'
