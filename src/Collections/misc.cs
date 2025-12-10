namespace Collections;

public static class misc
{
    
    // This should return a pretty unbalanced RrbList.
    public static RrbList<int> MakeUnbalanced(int length)
    {
        var list = new RrbList<int>(Enumerable.Range(0, length));

        for (int i = 0; i < list.Count; i += Constants.RRB_BRANCHING)
        {
            list = list.RemoveAt(i);
        }

        return list;
    }
}