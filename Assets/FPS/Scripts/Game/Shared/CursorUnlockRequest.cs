namespace Unity.FPS.Game
{
    public static class CursorUnlockRequest
    {
        private static bool s_IsActive;
        private static bool s_IsPriorityActive;

        public static bool IsActive => s_IsActive || s_IsPriorityActive;

        public static void SetActive(bool active)
        {
            s_IsActive = active;
        }

        public static void SetPriorityActive(bool active)
        {
            s_IsPriorityActive = active;
        }
    }
}
