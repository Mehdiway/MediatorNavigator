using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using Task = System.Threading.Tasks.Task;

namespace MediatorNavigator
{
    internal sealed class MediatorNavigator
    {
        public const int CommandId = 0x0100;

        public static readonly Guid CommandSet = new Guid("5193e04c-6ef9-441a-8a4f-94c25ff77f69");

        private readonly AsyncPackage package;

        private MediatorNavigator(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService =
                commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static MediatorNavigator Instance { get; private set; }

        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get { return this.package; }
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in MediatorNavigator's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService =
                await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new MediatorNavigator(package, commandService);
        }

        private async void Execute(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Retrieve the DTE (Development Tools Environment) service.
                var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
                if (dte == null)
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "Mediator Navigator",
                        "DTE service not found."
                    );
                    return;
                }
                // Get the currently active document.
                EnvDTE.Document activeDoc = dte.ActiveDocument;
                if (activeDoc == null)
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "Mediator Navigator",
                        "No active document."
                    );
                    return;
                }

                string filePath = activeDoc.FullName;
                string requestType = GetRequestType(filePath);
                if (string.IsNullOrEmpty(requestType))
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "Mediator Navigator",
                        "Could not determine IRequest type in the active document."
                    );
                    return;
                }

                // Search for the handler.
                HandlerLocation location = FindHandlerForRequest(dte, requestType);
                if (location == null)
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "Mediator Navigator",
                        $"No IRequestHandler found for {requestType}."
                    );
                    return;
                }

                // Open the file that contains the handler.
                Window window = dte.ItemOperations.OpenFile(location.FileName);
                if (window == null)
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "Mediator Navigator",
                        "Unable to open handler file."
                    );
                    return;
                }

                // Jump to the line where the handler is declared.
                TextSelection selection = (TextSelection)dte.ActiveDocument.Selection;
                selection.GotoLine(location.Line, false);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Mediator Navigator", $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Uses Roslyn to parse the file at <paramref name="filePath"/> and locate a public class or record
        /// declaration that implements IRequest.
        /// </summary>
        private string GetRequestType(string filePath)
        {
            string fileContent = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(fileContent);
            var root = syntaxTree.GetRoot();

            // Look for a public class or record declaration whose base list contains an IRequest (generic or not)
            var typeDeclaration = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .FirstOrDefault(td =>
                    td.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))
                    && td.BaseList != null
                    && td.BaseList.Types.Any(bt =>
                    {
                        // Extract the name of the base type (could be an IdentifierName or a GenericName)
                        string baseTypeName = bt.Type switch
                        {
                            IdentifierNameSyntax idName => idName.Identifier.Text,
                            GenericNameSyntax genName => genName.Identifier.Text,
                            _ => null,
                        };

                        return baseTypeName == "IRequest";
                    })
                );

            return typeDeclaration?.Identifier.Text;
        }

        /// <summary>
        /// Walks through each project in the solution to locate a file containing an IRequestHandler for the given request type.
        /// </summary>
        private HandlerLocation FindHandlerForRequest(DTE dte, string requestType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                HandlerLocation location = FindHandlerInProject(project, requestType);
                if (location != null)
                {
                    return location;
                }
            }
            return null;
        }

        private HandlerLocation FindHandlerInProject(EnvDTE.Project project, string requestType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project.ProjectItems == null)
            {
                return null;
            }

            foreach (ProjectItem item in project.ProjectItems)
            {
                HandlerLocation location = FindHandlerInProjectItem(item, requestType);
                if (location != null)
                {
                    return location;
                }
            }
            return null;
        }

        private HandlerLocation FindHandlerInProjectItem(ProjectItem item, string requestType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Recursively search through subitems.
            if (item.ProjectItems != null)
            {
                foreach (ProjectItem subItem in item.ProjectItems)
                {
                    HandlerLocation location = FindHandlerInProjectItem(subItem, requestType);
                    if (location != null)
                    {
                        return location;
                    }
                }
            }

            // Only examine C# source files.
            if (item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = item.FileNames[1];
                string fileContent = File.ReadAllText(filePath);
                var syntaxTree = CSharpSyntaxTree.ParseText(fileContent);
                var root = syntaxTree.GetRoot();

                // Look for class declarations that implement IRequestHandler<requestType, ...>
                var handlerDeclaration = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(cd =>
                        cd.BaseList != null
                        && cd.BaseList.Types.Any(bt =>
                        {
                            // Look for a generic interface named IRequestHandler
                            if (
                                bt.Type is GenericNameSyntax genericName
                                && genericName.Identifier.Text == "IRequestHandler"
                                && genericName.TypeArgumentList.Arguments.Count >= 1
                            )
                            {
                                // Compare the first type argument against the request type.
                                string firstTypeArg = genericName
                                    .TypeArgumentList.Arguments[0]
                                    .ToString()
                                    .Trim();
                                return firstTypeArg == requestType;
                            }
                            return false;
                        })
                    );

                if (handlerDeclaration != null)
                {
                    // Use Roslyn to get the line number (1-based) where the handler declaration starts.
                    var lineSpan = syntaxTree.GetLineSpan(handlerDeclaration.Span);
                    int lineNumber = lineSpan.StartLinePosition.Line + 1;
                    return new HandlerLocation { FileName = filePath, Line = lineNumber };
                }
            }

            return null;
        }

        /// <summary>
        /// Simple container class for storing a file path and line number.
        /// </summary>
        private class HandlerLocation
        {
            public string FileName { get; set; }
            public int Line { get; set; }
        }
    }
}
