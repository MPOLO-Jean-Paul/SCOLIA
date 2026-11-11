Option Explicit
Dim shell, fso, dossierLancement, dossier, programme, logs, script, powershell, commande, resultat, journal, lecteur, details
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
dossierLancement = fso.GetParentFolderName(WScript.ScriptFullName)
dossier = fso.GetParentFolderName(dossierLancement)
programme = fso.BuildPath(dossier, "programme")
script = fso.BuildPath(programme, "Lancer.ps1")
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
If Not fso.FileExists(script) Or Not fso.FileExists(fso.BuildPath(programme, "Application.ps1")) Or Not fso.FileExists(fso.BuildPath(programme, "Installer.ps1")) Or Not fso.FolderExists(fso.BuildPath(programme, "Corrections")) Then
    MsgBox "Un fichier du programme manque. Extrayez tout le nouveau ZIP dans un dossier vide.", vbExclamation, "SCOLIA"
    WScript.Quit 1
End If
If Not fso.FileExists(powershell) Then
    MsgBox "Windows PowerShell est introuvable sur cet ordinateur.", vbCritical, "SCOLIA"
    WScript.Quit 1
End If
logs = fso.BuildPath(dossier, "journaux")
On Error Resume Next
If Not fso.FolderExists(logs) Then fso.CreateFolder logs
If Err.Number <> 0 Then
    Err.Clear
    logs = fso.GetSpecialFolder(2)
End If
On Error GoTo 0
journal = fso.BuildPath(logs, "SCOLIA_Application_" & Replace(fso.GetTempName, ".tmp", ".txt"))
commande = """" & powershell & """ -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & script & """ -Mode Application -Journal """ & journal & """"
On Error Resume Next
resultat = shell.Run(commande, 0, True)
If Err.Number <> 0 Then
    MsgBox "Impossible de lancer le programme : " & Err.Description, vbCritical, "SCOLIA"
    WScript.Quit 1
End If
On Error GoTo 0
If resultat <> 0 Then
    details = "Le programme a rencontre une erreur au lancement (code " & resultat & ")."
    On Error Resume Next
    If fso.FileExists(journal) Then
        Set lecteur = fso.OpenTextFile(journal, 1, False, -1)
        If Err.Number = 0 Then
            details = lecteur.ReadAll
            lecteur.Close
        End If
    End If
    On Error GoTo 0
    MsgBox details & vbCrLf & vbCrLf & "Journal : " & journal & vbCrLf & "Les donnees Excel n'ont pas ete remplacees par ce lanceur.", vbCritical, "SCOLIA"
    WScript.Quit resultat
End If
