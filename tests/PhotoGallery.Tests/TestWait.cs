namespace PhotoGallery.Tests;

/// <summary>
/// Waits on work a screen starts and does not hand back, and fails rather than
/// hanging.
/// </summary>
/// <remarks>
/// Screens fire work and forget it, as they do in the app: a cover is decoded
/// after the wall draws, faces arrive after the picture, and a reload runs from
/// an event handler without anybody awaiting it. All a test can do is watch for
/// the result.
/// </remarks>
internal static class TestWait
{
    /// <param name="done">True once the work has landed.</param>
    /// <param name="what">Finishes the failure "Timed out waiting for ...".</param>
    /// <param name="attempts">How many checks, 10 ms apart, before the test fails.</param>
    public static async Task ForAsync(Func<bool> done, string what, int attempts = 200)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }
}
