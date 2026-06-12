# Quick Azure Service Bus health check
# Run: .\check-azure-sb.ps1

$ns = "thinkschool-quotes-bus"
$rg = "thinkschool-rg"
$topic = "quotes-topic"

Write-Host "`n=== Azure Service Bus Status ===" -ForegroundColor Cyan

# Namespace
$ns_status = az servicebus namespace show `
  --resource-group $rg `
  --name $ns `
  --query "{Status:status, Location:location, Sku:sku.name}" `
  --output json 2>&1 | ConvertFrom-Json

Write-Host "Namespace : $ns" -ForegroundColor White
Write-Host "Status    : $($ns_status.Status)" -ForegroundColor Green
Write-Host "Location  : $($ns_status.Location)"
Write-Host "Tier      : $($ns_status.Sku)"

Write-Host "`n=== Subscriptions on '$topic' ===" -ForegroundColor Cyan

$subs = az servicebus topic subscription list `
  --resource-group $rg `
  --namespace-name $ns `
  --topic-name $topic `
  --query "[].{Name:name, Status:status, Active:countDetails.activeMessageCount, DLQ:countDetails.deadLetterMessageCount, MaxDelivery:maxDeliveryCount}" `
  --output json 2>&1 | ConvertFrom-Json

foreach ($sub in $subs) {
    $dlqFlag = if ($sub.DLQ -gt 0) { " ⚠ DLQ=$($sub.DLQ)" } else { "" }
    Write-Host "  $($sub.Name.PadRight(20)) Status=$($sub.Status)  Active=$($sub.Active)  DLQ=$($sub.DLQ)  MaxDelivery=$($sub.MaxDelivery)$dlqFlag"
}

Write-Host "`n=== Endpoint ===" -ForegroundColor Cyan
Write-Host "sb://$ns.servicebus.windows.net"
Write-Host ""
