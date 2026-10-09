namespace HavenOS.Files.CUI.Tests;

internal static class Program
{
	private static async Task<int> Main()
	{
		try
		{
			await FilesDomainContractTests.RunAllAsync().ConfigureAwait(false);
			await OriginalExistingStateStoreTests.RunAllAsync().ConfigureAwait(false);
            await OriginalAttachmentStateReadTests.RunAllAsync().ConfigureAwait(false);
            await BrowserDownloadRegistrationProviderTests.RunAllAsync().ConfigureAwait(false);
			Console.WriteLine("Files CUI domain contract checks passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}
}
