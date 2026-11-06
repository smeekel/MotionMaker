namespace MotionMaker.Core;

public sealed class EditHistory
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string _savedJson;
    private bool _dirty;

    public EditHistory(ProjectDocument document)
    {
        Document = document;
        _savedJson = ProjectSerializer.ToJson(document);
    }

    public ProjectDocument Document { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsDirty => _dirty || Capture() != _savedJson;

    public string Capture() => ProjectSerializer.ToJson(Document);

    public void Commit(string beforeJson)
    {
        var now = Capture();
        if (now == beforeJson)
        {
            return;
        }

        _undo.Add(beforeJson);
        if (_undo.Count > 100)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _dirty = true;
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redo.Add(Capture());
        Document = ProjectSerializer.FromJson(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _dirty = Capture() != _savedJson;
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undo.Add(Capture());
        Document = ProjectSerializer.FromJson(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _dirty = Capture() != _savedJson;
    }

    public void MarkSaved()
    {
        _savedJson = Capture();
        _dirty = false;
    }

    public void RevertUncommitted(string snapshot)
    {
        Document = ProjectSerializer.FromJson(snapshot);
    }

    public void Reset(ProjectDocument document, bool dirty = false)
    {
        Document = document;
        _undo.Clear();
        _redo.Clear();
        _savedJson = Capture();
        _dirty = dirty;
    }
}
