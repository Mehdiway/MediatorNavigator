# Mediator Navigator
A Visual Studio 2022 extension to quickly navigate from an `IRequest` to its corresponding `IRequestHandler`.

## Features
- Instantly locate the handler (`IRequestHandler<TRequest, TResponse>`) for a given `IRequest<TResponse>`.
- Uses **Roslyn** for accurate C# code analysis instead of regex.
- Supports **Blazor, ASP.NET Core, and any MediatR-based projects**.
- Works in **Visual Studio 2022** (Enterprise, Professional, and Community).

## Demo
![Mediator Navigator Preview](https://github.com/Mehdiway/MediatorNavigator/blob/master/preview.png)

## Installation
### From the Visual Studio Marketplace
- Download & install from the **Visual Studio Marketplace** : [Mediator Navigator](https://marketplace.visualstudio.com/items?itemName=MehdiArmachi.MediatorNavigator)

### Manual Installation
1. Clone the repository
2. Open `MediatorNavigator.sln` in **Visual Studio 2022**.
3. Build the solution.
4. Install the `.vsix` package from the `bin/Debug` or `bin/Release` folder.

## Usage
1. Open a C# file containing an **IRequest**.
2. Click **Tools > Go to IRequestHandler** in Visual Studio's menu.
3. The extension will:
- Find the corresponding `IRequestHandler<TRequest, TResponse>`.
- Open the file containing the handler.
- Jump to the exact line where it’s defined.

## Configuration
This extension works out-of-the-box but can be customized in **Visual Studio’s Extension Manager**.

## Development
### Prerequisites
- **Visual Studio 2022** with the **Extensibility Development** workload.
- .NET 7+ SDK
- **MediatR** installed in your project.

### Packaging the VSIX
```msbuild /t:rebuild /p:Configuration=Release```

### Contributing

Contributions are welcome! Feel free to submit a PR or open an issue.