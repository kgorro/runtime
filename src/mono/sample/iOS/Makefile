MONO_CONFIG?=Debug
MONO_ARCH?=x64
DOTNET := ../../../../dotnet.sh
USE_LLVM=true
AOT?=false
TARGET?=iOSSimulator
DEPLOY_AND_RUN?=true
APP_SANDBOX?=false

#If DIAGNOSTIC_PORTS is enabled, RUNTIME_COMPONENTS must also be enabled.
#If RUNTIME_COMPONENTS is enabled, DIAGNOSTIC_PORTS is optional.
#If RUNTIME_COMPONENTS is enabled, DIAGNOSTIC_PORTS is disabled, use DOTNET_DiagnosticPorts when launching application to enable diagnostics.
#RUNTIME_COMPONENTS=diagnostics_tracing
#DIAGNOSTIC_PORTS=127.0.0.1:9000,nosuspend
#DIAGNOSTIC_PORTS=127.0.0.1:9000,suspend
#DIAGNOSTIC_PORTS=$(DOTNET_DiagnosticPorts)

all: runtimepack run

TOOLS_DIR=../../../tasks
appbuilder:
	$(DOTNET) build -c Debug $(TOOLS_DIR)/AotCompilerTask/MonoAOTCompiler.csproj
	$(DOTNET) build -c Debug $(TOOLS_DIR)/AppleAppBuilder/AppleAppBuilder.csproj

runtimepack:
	../../../../build.sh Mono+Libs -os $(TARGET)  -arch $(MONO_ARCH) -c $(MONO_CONFIG)

run: clean appbuilder
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	/p:TargetOS=iOS \
	/p:TargetArchitecture=$(MONO_ARCH) \
	/p:UseLLVM=$(USE_LLVM) \
	/p:ForceAOT=$(AOT) \
	'/p:RuntimeComponents="$(RUNTIME_COMPONENTS)"' \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	'/p:DiagnosticPorts="$(DIAGNOSTIC_PORTS)"'

run-sim: clean appbuilder
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	/p:TargetOS=iOSSimulator \
	/p:TargetArchitecture=$(MONO_ARCH) \
	/p:UseLLVM=$(USE_LLVM) \
	/p:ForceAOT=$(AOT) \
	'/p:RuntimeComponents="$(RUNTIME_COMPONENTS)"' \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	'/p:DiagnosticPorts="$(DIAGNOSTIC_PORTS)"'

build-appbundle: clean appbuilder
	$(DOTNET) publish -c $(MONO_CONFIG) /p:TargetOS=$(TARGET) /p:TargetArchitecture=$(MONO_ARCH) \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	/p:UseLLVM=$(USE_LLVM) /p:ForceAOT=$(AOT) /bl \

run-catalyst:
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	/p:TargetOS=MacCatalyst \
	/p:TargetArchitecture=$(MONO_ARCH) \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	/p:UseLLVM=False \
	/p:ForceAOT=True \
	/p:EnableAppSandbox=$(APP_SANDBOX)

run-sim-interp: clean appbuilder
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	/p:TargetOS=iOSSimulator \
	/p:TargetArchitecture=$(MONO_ARCH) \
	/p:UseLLVM=$(USE_LLVM) \
	/p:ForceAOT=$(AOT) \
	/p:MonoForceInterpreter=true \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	'/p:RuntimeComponents="$(RUNTIME_COMPONENTS)"' \
	'/p:DiagnosticPorts="$(DIAGNOSTIC_PORTS)"'

run-catalyst-interp:
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	/p:TargetOS=MacCatalyst \
	/p:TargetArchitecture=$(MONO_ARCH) \
	/p:UseLLVM=False \
	'/p:DeployAndRun="$(DEPLOY_AND_RUN)"' \
	/p:ForceAOT=True \
	/p:EnableAppSandbox=$(APP_SANDBOX) \
	/p:MonoForceInterpreter=true

clean:
	rm -rf bin
