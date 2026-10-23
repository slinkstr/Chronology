using Chronology.Tools.ConfigDocGen;

var readmePath = args.Length > 0 ? args[0] : "README.md";
ConfigDocGenerator.WriteSection(readmePath);
