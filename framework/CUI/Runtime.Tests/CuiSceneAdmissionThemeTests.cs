using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiSceneAdmissionThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preference_revision_retries_when_admission_is_lost_during_publication(bool revokeInCallback)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var source = new Source();
            using var preference = new CuiThemePreferenceContext(source);
            var scene = new CuiNativeScene("app.boards", "Board", "Boards",
                new CuiRichParser().Parse("<Text Text='Retained board'/>"), new CuiViewModel(), new CuiViewModel(), null!)
                { ThemePreference = preference };
            var canPublish = true;
            var revoke = true;
            var callbacks = 0;
            using var binding = new CuiSceneThemeBinding(scene, () => canPublish, _ =>
            {
                callbacks++;
                if (revoke && revokeInCallback) canPublish = false;
            });
            var owner = new Border { Resources = binding.Resources };
            owner.ResourcesChanged += (_, _) =>
            {
                if (revoke && !revokeInCallback) canPublish = false;
            };

            source.Set(CuiAppearance.Bright);
            Assert.Equal(revokeInCallback ? 1 : 0, callbacks);
            Assert.False(canPublish);
            revoke = false;
            canPublish = true;
            binding.SynchronizeCurrent();
            Assert.Equal(revokeInCallback ? 2 : 1, callbacks);
            Assert.True(binding.Resources.TryGetResource("CuiAppearance", null, out var appearance));
            Assert.Equal("Bright", appearance);
            binding.SynchronizeCurrent();
            Assert.Equal(revokeInCallback ? 2 : 1, callbacks);
        }, CancellationToken.None);
    }

    private sealed class Source : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } =
            new("Glow", 0, "board-theme-admission", false, new(), CuiAppearance.Dark);
        public event EventHandler? Changed;
        public void Set(CuiAppearance appearance)
        {
            Current = Current with { Revision = Current.Revision + 1, Appearance = appearance };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
