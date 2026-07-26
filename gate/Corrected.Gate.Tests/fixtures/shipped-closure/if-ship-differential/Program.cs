namespace IfShipDifferential
{
    public sealed class Live
    {
#if SHIP
        // Executable content that is INVISIBLE to a static no-defines parse but LIVE under
        // the build's real DefineConstants (SHIP). A method body is a BlockSyntax => reject.
        public int Detonate()
        {
            return 1;
        }
#endif
    }
}
