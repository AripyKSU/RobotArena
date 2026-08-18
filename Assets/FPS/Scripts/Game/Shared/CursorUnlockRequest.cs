namespace Unity.FPS.Game
{
    public static class CursorUnlockRequest
    {
        public static bool IsActive { get; private set; }

        public static void SetActive(bool active)
        {
            IsActive = active;
        }
    }
}
