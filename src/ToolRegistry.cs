using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace PSConverter
{
    [DataContract]
    public sealed class FileFingerprint
    {
        [DataMember] public string Path;
        [DataMember] public string Sha256;
    }
    [DataContract]
    public sealed class ApprovedTool
    {
        [DataMember] public string Name;
        [DataMember] public List<FileFingerprint> Files;
    }
    [DataContract]
    public sealed class AppConfiguration
    {
        [DataMember] public List<ApprovedTool> Tools;
        [DataMember] public CodecOptions Options;
        public AppConfiguration() { Tools = new List<ApprovedTool>(); Options = new CodecOptions(); }
    }

    internal sealed class ToolRegistry
    {
        private readonly AppConfiguration configuration;
        private readonly List<ApprovedTool> bundled;
        public ToolRegistry(AppConfiguration configuration, string applicationDirectory = null)
        {
            this.configuration = configuration;
            string root = PathSafety.LocalPath(applicationDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
            using (Stream resource = typeof(ToolRegistry).Assembly.GetManifestResourceStream("codec.catalog"))
            {
                if (resource == null) throw new InvalidOperationException("The application codec catalog is missing.");
                bundled = (List<ApprovedTool>)new DataContractJsonSerializer(typeof(List<ApprovedTool>)).ReadObject(resource);
            }
            // Resolve the embedded, hash-pinned catalog against this extraction location, never a saved absolute path.
            foreach (ApprovedTool tool in bundled)
                foreach (FileFingerprint file in tool.Files) file.Path = PathSafety.LocalPath(Path.Combine(root,file.Path));
        }
        private ApprovedTool Resolve(string name)
        {
            ApprovedTool custom = configuration.Tools.FirstOrDefault(t => t.Name == name);
            if (custom != null && custom.Files != null && custom.Files.Count > 0 && custom.Files.All(f => File.Exists(f.Path))) return custom;
            return bundled.FirstOrDefault(t => t.Name == name);
        }
        public bool Has(string name)
        {
            ApprovedTool tool = Resolve(name);
            return tool != null && tool.Files.All(f => File.Exists(f.Path));
        }

        public static string Hash(Stream stream)
        {
            stream.Position = 0;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static ApprovedTool Inspect(string name, string executable)
        {
            string path = PathSafety.LocalPath(executable);
            if (!string.Equals(System.IO.Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select " + name + " for this tool slot.");
            var files = new List<FileFingerprint>();
            var candidates = new List<string> { path };
            // Pin adjacent native dependencies as well as the executable; a hash is identity, not a safety certificate.
            candidates.AddRange(Directory.GetFiles(System.IO.Path.GetDirectoryName(path), "*.dll"));
            candidates.AddRange(Directory.GetFiles(System.IO.Path.GetDirectoryName(path), "*.manifest"));
            string sidecar = path + ".config";
            if (File.Exists(sidecar)) candidates.Add(sidecar);
            foreach (string candidate in candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string local = PathSafety.LocalPath(candidate);
                using (var stream = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read))
                    files.Add(new FileFingerprint { Path = local, Sha256 = Hash(stream) });
            }
            return new ApprovedTool { Name = name, Files = files };
        }

        public void Approve(ApprovedTool tool)
        { configuration.Tools.RemoveAll(t => t.Name == tool.Name); configuration.Tools.Add(tool); }

        public ToolLease Open(string name)
        {
            ApprovedTool tool = Resolve(name);
            if (tool == null || !Has(name)) throw new InvalidOperationException("Encoder files are missing. Extract the complete portable ZIP, including its codecs folder.");
            return new ToolLease(tool);
        }
    }

    internal sealed class ToolLease : IDisposable
    {
        private readonly List<FileStream> streams = new List<FileStream>();
        public readonly string Executable;
        public ToolLease(ApprovedTool tool)
        {
            if (tool.Files == null || tool.Files.Count == 0) throw new InvalidOperationException("Tool configuration is incomplete.");
            Executable = PathSafety.LocalPath(tool.Files[0].Path);
            // The executable is not necessarily the first sorted path. Bind it by its approved slot name.
            FileFingerprint executable = tool.Files.FirstOrDefault(f => string.Equals(Path.GetFileName(f.Path), tool.Name, StringComparison.OrdinalIgnoreCase));
            if (executable == null) throw new InvalidOperationException("Tool executable is missing from its approved manifest.");
            Executable = PathSafety.LocalPath(executable.Path);
            try
            {
                foreach (FileFingerprint fingerprint in tool.Files)
                {
                    string path = PathSafety.LocalPath(fingerprint.Path);
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    streams.Add(stream);
                    if (!string.Equals(ToolRegistry.Hash(stream), fingerprint.Sha256, StringComparison.Ordinal))
                        throw new InvalidOperationException("A codec file changed. Review and approve the tool again.");
                }
                // Added neighboring DLLs can change the Windows loader's resolution, even when pinned files match.
                string directory = Path.GetDirectoryName(Executable);
                var expected = new HashSet<string>(tool.Files.Select(f => PathSafety.LocalPath(f.Path)), StringComparer.OrdinalIgnoreCase);
                foreach (string dependency in Directory.GetFiles(directory, "*.dll").Concat(Directory.GetFiles(directory, "*.manifest")))
                    if (!expected.Contains(PathSafety.LocalPath(dependency)))
                        throw new InvalidOperationException("The codec folder has new dependencies. Review and approve it again.");
                string sidecar = Executable + ".config";
                if (File.Exists(sidecar) && !expected.Contains(sidecar))
                    throw new InvalidOperationException("The codec configuration changed. Review and approve it again.");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (FileStream stream in streams) stream.Dispose(); streams.Clear(); }
    }

    internal static class ConfigurationStore
    {
        public static AppConfiguration Load(string path)
        {
            if (!File.Exists(path)) return new AppConfiguration();
            using (var stream = new FileStream(PathSafety.LocalPath(path), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 1024 * 1024) throw new InvalidDataException("Configuration is too large.");
                var config = (AppConfiguration)new DataContractJsonSerializer(typeof(AppConfiguration)).ReadObject(stream);
                if (config == null) throw new InvalidDataException("Configuration is empty.");
                if (config.Tools == null) config.Tools = new List<ApprovedTool>();
                if (config.Options == null) config.Options = new CodecOptions();
                return config;
            }
        }
        public static void Save(string path, AppConfiguration configuration)
        {
            path = PathSafety.LocalPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(AppConfiguration)).WriteObject(stream, configuration);
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }
    }
}
