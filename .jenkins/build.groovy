node {
    stage('Clean workspace') {
        cleanWs()
    }
    stage('Get source') {
        git branch: 'main', url: 'https://github.com/pauldeen79/ScriptCompiler/'
    }
    stage('Build solution') {
        sh 'dotnet restore ScriptCompiler.sln'
        sh 'dotnet build ScriptCompiler.sln --no-restore'
    }
    stage('Run tests') {
        sh 'dotnet add ./src/ScriptCompiler.Tests/ScriptCompiler.Tests.csproj package JUnitTestLogger --version 1.1.0'
        sh 'dotnet test ./src/ScriptCompiler.Tests/ScriptCompiler.Tests.csproj -c Release --no-restore --logger \"junit;LogFilePath=../../TestResults/1.0.0.$BUILD_NUMBER/results.xml\"'
    }
    stage('Create package') {
        sh 'dotnet pack ScriptCompiler.sln -v normal -c Release -o ./artifacts --no-restore --include-symbols --include-source -p:PackageVersion=1.0.$BUILD_NUMBER'
    }
    stage('Publish artifacts') {
        archiveArtifacts artifacts: 'artifacts/pauldeen79.ScriptCompiler.ScriptCompiler.*', followSymlinks: false
        junit "TestResults/1.0.0.${BUILD_NUMBER}/results.xml"
    }
}
