$path = "C:\Users\josh\Documents\GitHub\AutoCore\server-live.log"
while ($true) {
  if (Test-Path $path) {
    Get-Content -LiteralPath $path -Wait -Tail 5 -ErrorAction SilentlyContinue | ForEach-Object {
      if ($_ -match "MISSION-DIAG|AutoPatrol|2945|6518|PAD-HIT|PAD-REJECT|ADVANCE|listed=|FailMission|GrantMission|giveMission|removeMission") {
        $_
      }
    }
  } else {
    Start-Sleep -Seconds 1
  }
}
