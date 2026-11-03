# Lease-wrapper for the DesktopWasm build lock (with-build-lock.ps1 takes
# EXACTLY ONE child token with a dash-free path). Owned by merge-packaging.
dotnet test E:\AI\Code\DesktopWasm\tests\DesktopWasm.Packaging.Tests\DesktopWasm.Packaging.Tests.csproj --nologo -v q
exit $LASTEXITCODE
