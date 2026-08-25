using Iridium;
using Iridium.Installation.Installer;
using Iridium.Models.Minecraft;
using Iridium.Sample;

IridiumConfig.Configure(new IridiumContext());

await TestInstaller.RunAsync();