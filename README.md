# DexInstructionRunner

Disclaimer: This project is provided as-is and is intended only as a sample application to help you get started with building your own TeamViewer DEX instruction runner. No warranties, guarantees, or support are provided.

## Release Status

This project is currently an alpha sample. The downloadable executable is provided for convenience so developers can quickly test the baseline application and review how the source code works.

This is not a production-ready or supported TeamViewer product.

## Intended Use

DexInstructionRunner is intended to demonstrate a basic desktop workflow for:

* Connecting to a configured TeamViewer DEX / 1E platform
* Authenticating to the platform
* Loading available instructions
* Running an instruction against a selected device
* Viewing instruction results locally
* Providing a starting point for custom internal tooling

A focused desktop utility to browse, run, and monitor TeamViewer DEX instructions against explicitly selected endpoints. The application emphasizes safe targeting, fast execution, and operator-friendly workflows.

## Known Limitations

* Authentication flows may require additional testing in your environment.
* 2FA support may be incomplete or environment-dependent.
* Configuration is expected to be customized before real use.
* The alpha executable is primarily intended for Windows x64 testing.
* Code signing, installer packaging, auto-update, and production deployment handling are not included.

## What is TeamViewer DEX?

TeamViewer DEX provides proactive endpoint monitoring and automated remediation for digital employee experience use cases. This utility focuses on the instruction execution workflow: discover, parameterize, run, and review results.

<img src="Images/InstructionRunnerDetailTab.png" width="50%" />

## Features

### 🔎 Instruction Discovery

Load instructions and instruction packs directly from your DEX tenant.

Filter instructions by:

* Name
* Category
* Tags
* Description

This allows quick discovery of relevant instructions in large environments.

### 🧩 Dynamic Parameter Editor

When an instruction is selected, the application automatically generates a typed parameter form.

Features include:

* Automatic parameter detection
* Dropdowns for enumerated values
* Validation for required fields
* Default value population

This allows instructions to be run without manually constructing payloads.

### 🖥️ Safe FQDN-Based Targeting

Devices are primarily targeted using explicit FQDN selection.

Capabilities include:

* FQDN device search
* Primary user search
* Controlled device selection
* Maximum of 10 devices per execution

This model helps prevent accidental large-scale instruction execution.

### ▶️ Run and Monitor Instructions

Instructions can be executed directly from the UI.

The runner provides:

* Real-time execution status
* Per-device success or failure state
* Error visibility and diagnostics

Execution progress is streamed back to the interface for immediate feedback.

### 🧾 Results Export

Instruction results can be exported for analysis or reporting.

Supported formats:

* CSV
* TSV
* XLSX

Exports can be configured with row limits and default formats from the settings panel.

### ⚙️ Built-In Configuration Panel

The application includes a settings flyout for runtime configuration.

Available options include:

* Dev token reuse
* API troubleshooting logging
* Authentication refresh threshold
* Result display limits
* Export row limits
* Default export format
* Platform configuration

### 🌐 Platform Management

Multiple DEX platforms can be configured directly within the application.

Features include:

* Platform aliases
* Default platform selection
* Secure platform URL handling
* Inline platform editing

This allows users to quickly switch between environments.

## How it Works

### 1. Connect

The application authenticates to the DEX platform using interactive credentials.

### 2. Load Instructions

Available instructions are retrieved from the platform and cached for the session.

### 3. Parameterize

When an instruction is selected, a typed parameter form is generated automatically.

### 4. Select Targets

Devices are selected using FQDN search or explicit device selection.

### 5. Run Instruction

The application dispatches the instruction and tracks execution status per device.

### 6. Review Results

Results are displayed in the UI and can be exported for further analysis.

## Getting Started

### Option 1: Download the Alpha Executable

Download the latest alpha release ZIP from the GitHub Releases page.

Extract the ZIP file and run:
DexInstructionRunner.exe


The alpha executable is intended primarily for Windows x64 testing.

### Option 2: Clone and Build from Source

#### Prerequisites

* OS: Windows or macOS
* SDK: .NET 8 SDK
* UI Framework: Avalonia UI

#### Clone
git clone https://github.com/teamviewer/DexInstructionRunner.git
cd DexInstructionRunner


#### Restore and Build
dotnet restore
dotnet build -c Release -f net8.0


#### Optional Publish for Windows x64

dotnet publish .\DexInstructionRunner.csproj -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false

Adjust the runtime identifier for your platform if needed.
