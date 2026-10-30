// Password text-pointer cluster (PresentationFramework
// System.Windows.Documents.ITextRange/ITextSelection and
// System.Windows.Controls.PasswordTextPointer, shim, not port).
//
// Covered gaps: the MaterialDesign PasswordBoxRevealTextBoxBehavior resolves
// these four shapes by name in its static constructor (all verified against
// the real WPF assemblies with a net10.0-windows oracle probe):
//   PasswordBox.Select(int, int) is an internal instance method;
//   PasswordBox.Selection is an internal instance ITextSelection property;
//   ITextSelection extends ITextRange (Start/End text pointers);
//   PasswordTextPointer.Offset has a non-public getter.
// The engine hosts the live password selection behind them (see
// PasswordBox.Selection/Select in EffectsHosts.cs).
//
// Retained limit: document-range operations (TextRange text editing,
// TextPointer gravity/direction navigation) stay a boundary; only the
// anchor/offset surface the reveal behavior reflects over is modeled.

namespace System.Windows.Documents
{
    /// <summary>Text position (matches System.Windows.Documents.ITextPointer;
    /// the engine models the offset surface the password behavior needs).</summary>
    public interface ITextPointer
    {
    }

    /// <summary>Text range (matches System.Windows.Documents.ITextRange;
    /// Start/End verified against the native oracle).</summary>
    public interface ITextRange
    {
        /// <summary>Range start (matches ITextRange.Start).</summary>
        ITextPointer Start { get; }

        /// <summary>Range end (matches ITextRange.End).</summary>
        ITextPointer End { get; }
    }

    /// <summary>Live selection (matches System.Windows.Documents.ITextSelection,
    /// which extends ITextRange like native).</summary>
    public interface ITextSelection : ITextRange
    {
    }
}

namespace System.Windows.Controls
{
    /// <summary>Password position (matches System.Windows.Controls.PasswordTextPointer;
    /// Offset is non-public like native, resolved by the reveal behavior with
    /// Instance|NonPublic reflection).</summary>
    public sealed class PasswordTextPointer : System.Windows.Documents.ITextPointer
    {
        /// <summary>Positions the pointer (offset clamps into the password).</summary>
        internal PasswordTextPointer(int offset, int length)
        {
            Offset = Math.Clamp(offset, 0, Math.Max(0, length));
        }

        /// <summary>Positions the pointer (offset used as-is).</summary>
        internal PasswordTextPointer(int offset)
        {
            Offset = offset;
        }

        /// <summary>Char offset into the password (non-public like native).</summary>
        internal int Offset { get; }
    }
}
