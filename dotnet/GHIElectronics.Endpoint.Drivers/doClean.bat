@echo off
echo Cleaning....

for /f "tokens=*" %%a in ('dir /b /s /a:d ".\"') do (
	if /i "%%~nxa"=="bin" (
		echo Deleting...  %%a
		rd /q /s %%a			
	)
	
	if /i "%%~nxa"=="obj" (
		echo Deleting...  %%a
		rd /q /s %%a		
	)	
)

pushd output
	DEL /Q *.nupkg
popd


