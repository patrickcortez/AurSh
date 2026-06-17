$content = Get-Content -Path src/core/MathEvaluator.cs -Raw
$content = $content -replace "\bdouble\b", "long"
$content = $content -replace "long\.TryParse\(token, NumberStyles\.Any,", "long.TryParse(token, NumberStyles.Integer | NumberStyles.AllowLeadingSign,"
$content = $content -replace "long\.TryParse\(envVal, NumberStyles\.Any,", "long.TryParse(envVal, NumberStyles.Integer | NumberStyles.AllowLeadingSign,"
$content = $content -replace "long left = Math\.Pow\(left, right\);", "long left = (long)Math.Pow(left, right);"
Set-Content -Path src/core/MathEvaluator.cs -Value $content
