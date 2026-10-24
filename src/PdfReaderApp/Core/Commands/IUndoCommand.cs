namespace PdfReaderApp.Core.Commands;

public interface IUndoCommand
{
    void Execute();
    void Undo();
}
