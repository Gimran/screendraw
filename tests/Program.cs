using ScreenDraw;

string root = Path.Combine(Path.GetTempPath(), "ScreenDraw-SaveTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Write(string dir) => File.WriteAllText(Path.Combine(dir, "image.png"), "test-image");
string? Unexpected(string? dir, Exception ex) => throw new Exception("Unexpected recovery: " + dir, ex);
try
{
    Test("Missing nested directory is recreated", () => {
        string dir = Path.Combine(root, "new", "nested");
        Check(SaveLocation.Save(dir, Write, Unexpected) == dir, "Wrong result");
        Check(File.ReadAllText(Path.Combine(dir, "image.png")) == "test-image", "File not saved");
    });
    Test("Existing directory saves normally", () => {
        Check(SaveLocation.Save(root, Write, Unexpected) == root, "Save failed");
    });
    foreach (string? invalid in new string?[] { null, "", "relative-folder", "C:\bad\0path".Replace("0", "\0") })
        Test("Invalid path cancels without writing: " + (invalid ?? "null"), () => {
            bool wrote = false; int prompts = 0;
            var result = SaveLocation.Save(invalid, _ => wrote = true, (_, _) => { prompts++; return null; });
            Check(result == null && !wrote && prompts == 1, "Cancel was treated as success");
        });
    string occupied = Path.Combine(root, "occupied");
    File.WriteAllText(occupied, "keep me");
    Test("File instead of directory: choose another folder and retry", () => {
        string recovery = Path.Combine(root, "recovered"); int prompts = 0;
        Check(SaveLocation.Save(occupied, Write, (_, _) => { prompts++; return recovery; }) == recovery, "No recovery");
        Check(prompts == 1 && File.Exists(Path.Combine(recovery, "image.png")), "Retry not saved");
        Check(File.ReadAllText(occupied) == "keep me", "Original file overwritten");
    });
    Test("Unavailable drive can be cancelled", () => {
        string drive = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => ((char)c) + ":\\")
            .First(d => !Directory.Exists(d));
        bool wrote = false;
        Check(SaveLocation.Save(drive + "Screenshots", _ => wrote = true, (_, ex) => {
            Check(ex is DirectoryNotFoundException, "Wrong failure"); return null;
        }) == null && !wrote, "Wrote to missing drive");
    });
    Test("Write access denied: retry then cancel", () => {
        int attempts = 0, prompts = 0;
        Check(SaveLocation.Save(root, _ => { attempts++; throw new UnauthorizedAccessException("denied"); },
            (_, _) => ++prompts == 1 ? root : null) == null, "False success");
        Check(attempts == 2 && prompts == 2, "Wrong retry count");
    });
    Test("Locked output file: preserve file and recover elsewhere", () => {
        string file = Path.Combine(root, "image.png");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string recovery = Path.Combine(root, "unlocked");
        Check(SaveLocation.Save(root, Write, (_, _) => recovery) == recovery, "No locked-file recovery");
        Check(File.Exists(Path.Combine(recovery, "image.png")), "No recovered file");
    });
    Test("Folder disappears between validation and write", () => {
        string race = Path.Combine(root, "race"); int attempts = 0;
        string result = SaveLocation.Save(race, d => {
            if (++attempts == 1) Directory.Delete(d);
            Write(d);
        }, (_, _) => race)!;
        Check(result == race && attempts == 2 && File.Exists(Path.Combine(race, "image.png")), "Race not recovered");
    });
    Test("Non-filesystem failure is not swallowed", () => {
        bool caught = false;
        try { SaveLocation.Save(root, _ => throw new InvalidOperationException("clipboard"), Unexpected); }
        catch (InvalidOperationException) { caught = true; }
        Check(caught, "Unexpected failure swallowed");
    });
    Console.WriteLine($"{passed} tests passed");
}
finally { Directory.Delete(root, recursive: true); }
