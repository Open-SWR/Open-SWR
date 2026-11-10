namespace OpenSWR.Sim;

/// <summary>A request slot per side: the script action number of C-3PO (Alliance) and of IMP-22 (Empire). 0 = no slot on that side.</summary>
public readonly record struct SideSlot(int C3po, int Imp)
{
    /// <summary>The action number for the side.</summary>
    public int For(bool imp) => imp ? Imp : C3po;
}
