using CommandLine;

namespace MattRuwe.Dedupper
{
    class CommonOptions
    {
        [Option('s', "source", Required = true, HelpText = "The path for the root folder to start searching")]
        public string SourcePath { get; set; }
    }

    [Verb("find")]
    class FindDupsOptions : CommonOptions
    {

    }

    [Verb("verify")]
    class VerifyDupsOptions
    {

    }

    [Verb("choose")]
    class ChooseDupsOptions : CommonOptions
    {
        [Option('b', "backup", Required = true, HelpText = "The path to store files at after they have been found to be dups.")]
        public string BackupPath { get; set; }

        [Option('m', "match", Required = false, HelpText = "A regular expression pattern to match against the files that should be choosen.")]
        public string Match { get; set; }
    }
}