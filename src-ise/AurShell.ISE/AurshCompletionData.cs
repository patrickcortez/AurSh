using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace AurShell.ISE
{
    public class AurshCompletionData : ICompletionData
    {
        public AurshCompletionData(string text)
        {
            Text = text;
        }

        public IImage? Image => null;

        public string Text { get; }

        public object Content => Text;

        public object Description => "AurSh Command";

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
