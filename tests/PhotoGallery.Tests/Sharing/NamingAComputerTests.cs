using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Another computer can be given a name here that somebody will recognise, in
/// place of the one it calls itself.
/// </summary>
/// <remarks>
/// The name is this computer's alone. Nothing a sync reads or writes touches
/// it, so it outlives every file the other computer leaves, and a planted file
/// cannot take it by calling itself it.
/// </remarks>
public sealed class NamingAComputerTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ANameGivenHere_IsWhatTheSyncScreenSays_AndOutlivesTheNextSync()
    {
        await DadHasSyncedWithMumAsync();

        MachineNaming naming = await Dad.Naming.HandleAsync(Mum.MachineId, "  Kitchen laptop  ");

        Assert.Equal(MachineNaming.Named, naming);

        // A later file from Mum renames her row to what she calls herself, as
        // it always has; it leaves the name given here alone.
        Mum.Person("Ben");
        await Mum.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        MachineStanding mum = await MumOnDadsScreenAsync();
        Assert.Equal("Kitchen laptop", mum.Name);
        Assert.Equal("Mum's laptop", mum.OwnName);

        KnownMachine row = await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == Mum.MachineId);
        Assert.Equal("Mum's laptop", row.Name);
        Assert.Equal("Kitchen laptop", row.FamiliarName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mum's laptop")]
    public async Task ClearingTheBoxOrTypingItsOwnName_GoesBackToWhatItCallsItself(string typed)
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        MachineNaming naming = await Dad.Naming.HandleAsync(Mum.MachineId, typed);

        Assert.Equal(MachineNaming.Named, naming);
        Assert.Null((await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == Mum.MachineId)).FamiliarName);
        Assert.Equal("Mum's laptop", (await MumOnDadsScreenAsync()).Name);
    }

    [Fact]
    public async Task ANameLongerThanTheColumn_IsCutRatherThanRefused()
    {
        await DadHasSyncedWithMumAsync();

        await Dad.Naming.HandleAsync(Mum.MachineId, new string('k', KnownMachine.NameLength + 40));

        Assert.Equal(KnownMachine.NameLength, (await MumOnDadsScreenAsync()).Name.Length);
    }

    /// <summary>
    /// Two rows reading the same are what somebody answers for twice, so a
    /// name another computer already goes by here is not kept - whether that
    /// is its own or one given to it here.
    /// </summary>
    [Theory]
    [InlineData(null, "Gran's laptop")]
    [InlineData(null, "GRAN'S LAPTOP")]
    [InlineData("Study desktop", "study desktop")]
    [InlineData("Study desktop", "gran's laptop")]
    public async Task ANameAnotherComputerGoesBy_IsNotKept(string? gransNameHere, string typed)
    {
        await DadHasSyncedWithMumAsync();
        Library gran = _house.Add("Gran's laptop");
        Assert.Equal(MachineNaming.Named, await Dad.Naming.HandleAsync(gran.MachineId, gransNameHere));

        MachineNaming naming = await Dad.Naming.HandleAsync(Mum.MachineId, typed);

        Assert.Equal(MachineNaming.NameTaken, naming);
        Assert.Null((await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == Mum.MachineId)).FamiliarName);
    }

    /// <summary>
    /// Going back to what a computer calls itself is how a look-alike a file
    /// made is undone, so it is never refused - not even while a copy calls
    /// itself that same name.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Mum's laptop")]
    public async Task GoingBackToItsOwnName_IsKept_WhileACopyCallsItselfThatToo(string typed)
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Library planted = _house.Stranger("Mum's laptop");
        planted.Person("Ana");
        await planted.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        MachineNaming naming = await Dad.Naming.HandleAsync(Mum.MachineId, typed);

        Assert.Equal(MachineNaming.Named, naming);
        Assert.Null((await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == Mum.MachineId)).FamiliarName);
    }

    [Fact]
    public async Task ThisComputer_AndOneNeverMet_CannotBeNamed()
    {
        await DadHasSyncedWithMumAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Dad.Naming.HandleAsync(Dad.MachineId, "Mine"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Dad.Naming.HandleAsync(Guid.NewGuid(), "Nobody's"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Dad.Naming.HandleAsync(Guid.Empty, "Nobody's"));
    }

    /// <summary>
    /// The name is kept on the computer it was given on, and nowhere in the
    /// sync folder.
    /// </summary>
    [Fact]
    public async Task TheNameNeverReachesTheSyncFolderOrTheOtherComputer()
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        await Dad.Sharing.HandleAsync();
        await Mum.Sharing.HandleAsync();

        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(
                     _house.SharedFolder, "*", SearchOption.AllDirectories))
        {
            files.Add(await ReadableAsync(file));
        }

        // The search can see a name at all: read as they lie, gzipped, the
        // files would pass whatever they held.
        Assert.Contains(files, text => text.Contains("Dad's laptop", StringComparison.Ordinal));
        Assert.DoesNotContain(files, text => text.Contains("Kitchen laptop", StringComparison.Ordinal));

        // And Dad still tells the folder what he calls himself.
        DecisionSet dads = Assert.Single(
            (await Mum.Exchange.FetchAsync()).Sets, set => set.Machine.Id == Dad.MachineId);
        Assert.Equal("Dad's laptop", dads.Machine.Name);

        Assert.DoesNotContain(
            await Mum.Db.KnownMachines.AsNoTracking().ToListAsync(),
            machine => machine.CalledHere == "Kitchen laptop");
    }

    [Fact]
    public async Task TheNoticeOfNewChanges_UsesTheNameGivenHere()
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Mum.Person("Ben");
        await Mum.Publishing.HandleAsync();

        SharedUpdate update = await Dad.Looking.HandleAsync();
        Assert.Equal(["Kitchen laptop"], update.Machines);
    }

    [Fact]
    public async Task ANewComputerNamedBeforeItIsAnswered_IsWaitedOnByThatName()
    {
        Library stranger = _house.Stranger("LAPTOP-AB12CD");
        stranger.Person("Cara");
        await stranger.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        await Dad.Naming.HandleAsync(stranger.MachineId, "Spare laptop");
        ShareResult shared = await Dad.Sharing.HandleAsync();

        Assert.Equal(["Spare laptop"], shared.Merged.Unconfirmed);

        // Meeting it again renames its row to what its file says, and leaves
        // the name given here alone.
        KnownMachine row = await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == stranger.MachineId);
        Assert.Equal("Spare laptop", row.FamiliarName);
        Assert.Equal("LAPTOP-AB12CD", row.Name);
    }

    /// <summary>
    /// Saying whether to sync with a computer is about its changes, and leaves
    /// the name given it here as it was.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANameGivenHere_OutlivesSayingWhetherToSync(bool refusedFirst)
    {
        Library stranger = _house.Stranger("LAPTOP-AB12CD");
        stranger.Person("Cara");
        await stranger.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();
        await Dad.Naming.HandleAsync(stranger.MachineId, "Spare laptop");

        if (refusedFirst)
        {
            await Dad.Deciding.HandleAsync(stranger.MachineId, stranger.Name, take: false);
        }

        await Dad.Deciding.HandleAsync(stranger.MachineId, stranger.Name, take: true);

        KnownMachine row = await Dad.Db.KnownMachines.AsNoTracking()
            .SingleAsync(machine => machine.MachineId == stranger.MachineId);
        Assert.Equal(MachineTrust.Trusted, row.Trust);
        Assert.Equal("Spare laptop", row.FamiliarName);
        Assert.Equal("LAPTOP-AB12CD", row.Name);

        MachineStanding standing = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines,
            machine => machine.MachineId == stranger.MachineId);
        Assert.Equal("Spare laptop", standing.Name);
        Assert.Equal("LAPTOP-AB12CD", standing.OwnName);
    }

    /// <summary>
    /// A file calling itself the name this screen already uses for a laptop
    /// in the house is called out, as a file copying that laptop's own name is.
    /// </summary>
    [Fact]
    public async Task AFileCallingItselfTheNameGivenHere_IsCalledOut()
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Library planted = _house.Stranger("Kitchen laptop");
        planted.Person("Ana");
        await planted.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        MachineStanding asked = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.IsUnanswered);
        Assert.Equal(planted.MachineId, asked.MachineId);
        Assert.True(asked.SharesItsName);
        Assert.Contains(
            "Another computer here also goes by Kitchen laptop",
            SharingViewModel.AskingAbout(asked),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The look-alike warning goes on comparing what the computers call
    /// themselves, which is what a planted copy shares, whatever this screen
    /// calls the real one.
    /// </summary>
    [Fact]
    public async Task ACopyOfARenamedLaptopsOwnName_IsStillCalledOut()
    {
        await DadHasSyncedWithMumAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Library planted = _house.Stranger("Mum's laptop");
        planted.Person("Ana");
        await planted.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        MachineStanding asked = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.IsUnanswered);
        Assert.True(asked.SharesItsName);
        Assert.Contains(
            "Another computer here also goes by Mum's laptop",
            SharingViewModel.AskingAbout(asked),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewComputerCannotBeGivenTheNameOfAnother()
    {
        await DadHasSyncedWithMumAsync();
        Library stranger = _house.Stranger("LAPTOP-AB12CD");
        stranger.Person("Cara");
        await stranger.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        Assert.Equal(
            MachineNaming.NameTaken, await Dad.Naming.HandleAsync(stranger.MachineId, "mum's laptop"));
    }

    /// <summary>Mum has shared something, and Dad has synced and so written her down.</summary>
    private async Task DadHasSyncedWithMumAsync()
    {
        Mum.Person("Ana");
        await Mum.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();
    }

    /// <summary>
    /// A file in the sync folder as a person would search it: unpacked where it
    /// is gzipped, and with the escapes JSON writes - an apostrophe is ' -
    /// read back as the characters they stand for.
    /// </summary>
    private static async Task<string> ReadableAsync(string file)
    {
        byte[] bytes = await File.ReadAllBytesAsync(file);
        string text;

        if (bytes is [0x1F, 0x8B, ..])
        {
            using var packed = new MemoryStream(bytes);
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            text = await reader.ReadToEndAsync();
        }
        else
        {
            text = Encoding.UTF8.GetString(bytes);
        }

        return Regex.Replace(
            text,
            @"\\u([0-9A-Fa-f]{4})",
            escape => ((char)Convert.ToInt32(escape.Groups[1].Value, 16)).ToString());
    }

    private async Task<MachineStanding> MumOnDadsScreenAsync() =>
        Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines,
            machine => machine.MachineId == Mum.MachineId);

    public void Dispose() => _house.Dispose();
}
