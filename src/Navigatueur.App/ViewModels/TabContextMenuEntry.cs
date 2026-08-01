using System.Windows.Input;

namespace Navigatueur.App.ViewModels;

public sealed record TabContextMenuEntry(string Header, ICommand Command);
