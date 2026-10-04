using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScrcpyNet.Sample.ViewModels;
using System;
using System.IO;
using System.Text;

namespace ScrcpyNet.Test
{
    [TestClass]
    public class DeviceDatabaseTests
    {
        private string _dir = null!;

        [TestInitialize]
        public void CreateTempDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "scrcpynet-db-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void DeleteTempDir()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string DbPath => Path.Combine(_dir, "devices.db");

        private string WriteLegacyTxt(params string[] lines)
        {
            string path = Path.Combine(_dir, "Devices.txt");
            // UTF-8 BOM like the original file; caller decides about the trailing newline.
            File.WriteAllText(path, string.Join("\r\n", lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        [TestMethod]
        public void FirstRun_ImportsLegacyTxt()
        {
            // BOM is emitted by WriteLegacyTxt; blank line, 3-column line and a
            // double-space line must be skipped, exactly like the old FileReader.
            string txt = WriteLegacyTxt(
                "serial1 光光-刀客",
                "",
                "serial2 a b",
                "serial3  紫萍-109",
                "serial4 川哥哥4");

            var db = new DeviceDatabase(DbPath, new[] { txt });

            Assert.AreEqual(2, db.Count);
            var all = db.GetAll();
            Assert.AreEqual("serial1", all[0].Serial);
            Assert.AreEqual("光光-刀客", all[0].Name);
            Assert.AreEqual("serial4", all[1].Serial);
            Assert.AreEqual("川哥哥4", all[1].Name);
        }

        [TestMethod]
        public void FirstRun_WithoutLegacyTxt_StartsEmpty()
        {
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());

            Assert.AreEqual(0, db.Count);
            Assert.AreEqual(0, db.GetAll().Count);
        }

        [TestMethod]
        public void SecondRun_DoesNotReimport()
        {
            string txt = WriteLegacyTxt("serial1 one", "serial2 two");

            var db = new DeviceDatabase(DbPath, new[] { txt });
            Assert.IsTrue(db.Remove("serial1"));

            // Reopening the existing database must not re-run the txt migration.
            var reopened = new DeviceDatabase(DbPath, new[] { txt });
            Assert.AreEqual(1, reopened.Count);
            Assert.AreEqual("serial2", reopened.GetAll()[0].Serial);
        }

        [TestMethod]
        public void Import_SkipsKnownSerials()
        {
            string txt = WriteLegacyTxt("serial1 one", "serial2 two");
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());

            Assert.AreEqual(2, db.ImportFromTextFile(txt));
            // Re-importing the same file adds nothing.
            Assert.AreEqual(0, db.ImportFromTextFile(txt));
            Assert.AreEqual(2, db.Count);
        }

        [TestMethod]
        public void Import_MissingFile_ReturnsZero()
        {
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());

            Assert.AreEqual(0, db.ImportFromTextFile(Path.Combine(_dir, "nope.txt")));
        }

        [TestMethod]
        public void Add_Get_Rename_Remove()
        {
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());

            Assert.IsTrue(db.Add("serial1", "一号"));
            Assert.AreEqual(1, db.Count);

            // Duplicate serial is rejected.
            Assert.IsFalse(db.Add("serial1", "另一号"));
            Assert.AreEqual("一号", db.Get("serial1")!.Name);

            // Unknown serial cannot be renamed or removed.
            Assert.IsFalse(db.Rename("unknown", "x"));
            Assert.IsFalse(db.Remove("unknown"));

            // Renaming to the same name is a no-op.
            Assert.IsFalse(db.Rename("serial1", "一号"));
            Assert.IsTrue(db.Rename("serial1", " 新名字 "));
            Assert.AreEqual("新名字", db.Get("serial1")!.Name);

            Assert.IsTrue(db.Remove("serial1"));
            Assert.IsNull(db.Get("serial1"));
            Assert.AreEqual(0, db.Count);
        }

        [TestMethod]
        public void Add_EmptySerial_Throws()
        {
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());

            Assert.ThrowsExactly<ArgumentException>(() => db.Add("   ", "name"));
            Assert.ThrowsExactly<ArgumentException>(() => db.Rename("serial1", "  "));
        }

        [TestMethod]
        public void GetAll_KeepsInsertionOrder()
        {
            var db = new DeviceDatabase(DbPath, Array.Empty<string>());
            for (int i = 0; i < 5; i++)
                db.Add($"serial{i}", $"name{i}");

            var all = db.GetAll();
            for (int i = 0; i < 5; i++)
                Assert.AreEqual($"serial{i}", all[i].Serial);
        }
    }
}
