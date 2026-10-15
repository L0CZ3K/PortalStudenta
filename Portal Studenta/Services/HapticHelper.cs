using Microsoft.Maui.Devices;

namespace Portal_Studenta.Services
{
    public static class HapticHelper
    {
        public static void TriggerClick()
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
            }
            catch
            {
                // Ignoruj na platformach nieobsługujących haptyki (np. symulatory, wybrane Mac Catalyst)
            }
        }

        public static void TriggerSelection()
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
            }
            catch { }
        }

        public static void TriggerSuccess()
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
                }
            }
            catch { }
        }

        public static void TriggerWarning()
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
            }
            catch { }
        }
    }
}
