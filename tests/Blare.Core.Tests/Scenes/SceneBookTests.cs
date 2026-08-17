using Blight.Blare.Core.Scenes;

namespace Blare.Core.Tests.Scenes;

public class SceneBookTests
{
    private static Scene Gaming(double gameLevel = 100) =>
        new("Gaming",
        [
            new SceneLevel("game.exe", gameLevel, false),
            new SceneLevel("chrome.exe", 20, false),
        ]);

    [Fact]
    public void SavingAScene_MakesItRecallable()
    {
        var book = new SceneBook();
        book.Save(Gaming());

        Assert.Equal(20, book.Get("Gaming")!.For("chrome.exe")!.VolumePercent);
    }

    [Fact]
    public void SavingOverAScene_ReplacesItRatherThanAddingASecond()
    {
        var book = new SceneBook();
        book.Save(Gaming(100));
        book.Save(Gaming(70));

        Assert.Single(book.Scenes);
        Assert.Equal(70, book.Get("Gaming")!.For("game.exe")!.VolumePercent);
    }

    [Fact]
    public void SceneNamesAreMatchedIgnoringCase()
    {
        var book = new SceneBook();
        book.Save(Gaming());

        // Otherwise "gaming" silently becomes a second scene that shadows the first.
        Assert.NotNull(book.Get("gaming"));

        book.Save(new Scene("gaming", []));
        Assert.Single(book.Scenes);
    }

    [Fact]
    public void ReplacingAScene_KeepsItsPlaceInTheList()
    {
        var book = new SceneBook();
        book.Save(new Scene("First", []));
        book.Save(new Scene("Second", []));
        book.Save(new Scene("First", [new SceneLevel("a.exe", 50, false)]));

        Assert.Equal("First", book.Scenes[0].Name);
        Assert.Equal("Second", book.Scenes[1].Name);
    }

    [Fact]
    public void AnUnnamedScene_IsRejected()
    {
        var book = new SceneBook();
        book.Save(new Scene("  ", []));

        Assert.Empty(book.Scenes);
    }

    [Fact]
    public void RemovingAScene_LeavesTheRest()
    {
        var book = new SceneBook();
        book.Save(Gaming());
        book.Save(new Scene("Call", []));

        book.Remove("Gaming");

        Assert.Single(book.Scenes);
        Assert.Null(book.Get("Gaming"));
    }

    [Fact]
    public void RenamingAScene_KeepsItsLevels()
    {
        var book = new SceneBook();
        book.Save(Gaming());

        book.Rename("Gaming", "Evening");

        Assert.Null(book.Get("Gaming"));
        Assert.Equal(20, book.Get("Evening")!.For("chrome.exe")!.VolumePercent);
    }

    [Fact]
    public void RenamingToNothing_IsIgnored()
    {
        var book = new SceneBook();
        book.Save(Gaming());

        book.Rename("Gaming", "   ");

        Assert.NotNull(book.Get("Gaming"));
    }

    [Fact]
    public void AskingForAMissingScene_ReturnsNullRatherThanThrowing()
    {
        Assert.Null(new SceneBook().Get("nope"));
    }

    [Fact]
    public void AnAppNotInTheScene_HasNoLevel()
    {
        Assert.Null(Gaming().For("spotify.exe"));
    }

    [Fact]
    public void ScenesSurviveARoundTrip()
    {
        var book = new SceneBook();
        book.Save(Gaming());

        var restored = new SceneBook();
        restored.Restore(book.Scenes);

        Assert.Equal(book.Scenes.Count, restored.Scenes.Count);
        Assert.Equal(20, restored.Get("Gaming")!.For("chrome.exe")!.VolumePercent);
    }

    [Fact]
    public void RestoringDiscardsUnnamedScenesFromAHandEditedFile()
    {
        var book = new SceneBook();
        book.Restore([new Scene("Good", []), new Scene("", [])]);

        Assert.Single(book.Scenes);
    }
}
