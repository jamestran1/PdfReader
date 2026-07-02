namespace PdfReaderApp.Core.Commands;

public interface IUndoCommand
{
    void Execute();
    void Undo();
}

public class EditTextCommand : IUndoCommand
{
    private readonly int _pageIndex;
    private readonly int _charIndex;
    private readonly string _oldText;
    private readonly string _newText;

    public EditTextCommand(int pageIndex, int charIndex, string oldText, string newText)
    {
        _pageIndex = pageIndex;
        _charIndex = charIndex;
        _oldText = oldText;
        _newText = newText;
    }

    public void Execute()
    {
        // Logic to update PDF core object
        System.Diagnostics.Debug.WriteLine($"Executing Edit: '{_oldText}' -> '{_newText}' at {_charIndex}");
    }

    public void Undo()
    {
        // Logic to revert PDF core object
        System.Diagnostics.Debug.WriteLine($"Undoing Edit: '{_newText}' -> '{_oldText}' at {_charIndex}");
    }
}