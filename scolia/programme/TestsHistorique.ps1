if(-not (Get-Command Sort-ITMPaymentHistory -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'FonctionsGestion.ps1')}
function Test-ITMPaymentHistory {
    $rows=@(
        [pscustomobject]@{Date='02/10/2026';ID=3;Numero='B';Source='classeur-000001.xlsm';Row=5},
        [pscustomobject]@{Date='30/09/2026';ID=2;Numero='A';Source='classeur-000002.xlsm';Row=8},
        [pscustomobject]@{Date='30/09/2026';ID=1;Numero='A';Source='classeur-000001.xlsm';Row=30},
        [pscustomobject]@{Date='inconnue';ID=4;Numero='C';Source='classeur-000002.xlsm';Row=9}
    )
    $sorted=@(Sort-ITMPaymentHistory $rows)
    if(($sorted.ID -join ',') -ne '1,2,3,4'){throw 'Historique des paiements mal trié entre les classeurs.'}
    if(@(Sort-ITMPaymentHistory @()).Count -ne 0){throw 'Historique vide mal traité.'}
    if(@(Sort-ITMPaymentHistory @($rows[0])).Count -ne 1){throw 'Historique à un paiement mal traité.'}
    return 'Historique verifie : ordre des dates, departage par identifiant, classeurs multiples, date inconnue, liste vide et paiement unique.'
}
if($MyInvocation.InvocationName -ne '.') {Write-Output (Test-ITMPaymentHistory)}
