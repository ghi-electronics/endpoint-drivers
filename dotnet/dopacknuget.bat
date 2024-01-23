@echo off
SET OutputAssemblyName=%1
SET BuildMode=Release
SET OutputLocation=bin\%BuildMode%\net8.0
SET CurrentDir=%CD%

IF "%DoAssemblySign%" == "true" (
	pushd "%CurrentDir%"

	IF EXIST "..\..\..\output\%OutputAssemblyName%*.nupkg" (
		DEL /Q ..\..\..\output\%OutputAssemblyName%*.nupkg
	)

	signtool.exe sign /fd sha512 /f "%VsixSignerCertificatePath%" /p "%VsixSignerCertificatePassword%" /t "http://timestamp.digicert.com" /sha1 "%AssemblySignerCertificateSha1%" "%OutputLocation%\%OutputAssemblyName%.dll"
	nuget pack "%OutputAssemblyName%.nuspec" -Properties Configuration=%BuildMode% -OutputDirectory "..\..\..\output"
	xcopy /q /y ..\..\..\output\%OutputAssemblyName%*.nupkg %NugetPackerOutputDirectory%

	popd
)



