using System.Diagnostics;
using System.IO.Compression;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Licensing;

namespace PortfolioSaver.Installer;

internal static class Program
{
    private const string PayloadResourceName = "PortfolioSaver.Payload.zip";
    private static readonly string InstallerTitle = $"{AppIdentity.ApplicationName} Installer";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (!IsRunningAsAdministrator())
            {
                RelaunchElevated();
                return 0;
            }

            return RunInstall();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                InstallerTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated()
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve the installer executable path.");

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
        };

        Process.Start(startInfo);
    }

    private static int RunInstall()
    {
        string mitLicenseText = MitLicenseService.GetMitTextAsync().GetAwaiter().GetResult();
        using (LicenseAgreementForm agreementForm = new(mitLicenseText))
        {
            if (agreementForm.ShowDialog() != DialogResult.OK)
            {
                return 2;
            }
        }

        string stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "PortfolioSaverScreensaverInstaller-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingRoot);
            ExtractPayload(stagingRoot);

            string installScriptPath = Path.Combine(stagingRoot, "Install-PortfolioSaverScreensaver.ps1");
            if (!File.Exists(installScriptPath))
            {
                throw new FileNotFoundException("The embedded install script was not extracted.", installScriptPath);
            }

            ProcessStartInfo startInfo = new("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{installScriptPath}\" -StagingRoot \"{stagingRoot}\"",
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = stagingRoot
            };

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The installer could not start PowerShell.");

            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string details = BuildFailureMessage(standardOutput, standardError);
                throw new InvalidOperationException(details);
            }

            MessageBox.Show(
                $"{AppIdentity.ApplicationName} installed successfully." + Environment.NewLine +
                "Open Screen Saver Settings and choose PortfolioSaver.Screensaver.",
                InstallerTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return 0;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void ExtractPayload(string stagingRoot)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream payloadStream = assembly.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("The installer payload is missing from the executable.");

        string zipPath = Path.Combine(stagingRoot, "PortfolioSaverInstallerPayload.zip");
        using (FileStream zipFile = File.Create(zipPath))
        {
            payloadStream.CopyTo(zipFile);
        }

        ZipFile.ExtractToDirectory(zipPath, stagingRoot, overwriteFiles: true);
        File.Delete(zipPath);
    }

    private static string BuildFailureMessage(string standardOutput, string standardError)
    {
        StringBuilder builder = new();
        builder.AppendLine("The screensaver installer did not complete.");

        string combined = string.Join(
            Environment.NewLine,
            new[] { standardOutput, standardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(combined))
        {
            builder.AppendLine();
            builder.AppendLine(combined.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed class LicenseAgreementForm : Form
    {
        private readonly RichTextBox _licenseTextBox;
        private readonly CheckBox _agreeCheckBox;
        private readonly Button _acceptButton;
        private readonly Label _scrollStatusLabel;
        private bool _didReachBottom;

        public LicenseAgreementForm(string licenseText)
        {
            Text = $"{AppIdentity.LicenseName} Agreement";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 900;
            Height = 700;

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 7
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label heading = new()
            {
                Text = AppIdentity.ApplicationName,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true
            };

            Label metadata = new()
            {
                Text = $"Publisher: {AppIdentity.PublisherName}   |   Author: {AppIdentity.AuthorName}",
                AutoSize = true
            };

            LinkLabel officialLink = new()
            {
                AutoSize = true,
                Text = $"Open official {AppIdentity.LicenseName} page: {AppIdentity.OfficialLicenseUrl}"
            };
            officialLink.LinkClicked += (_, _) => OpenOfficialLicense();

            _licenseTextBox = new RichTextBox
            {
                Text = licenseText ?? string.Empty,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true
            };
            _licenseTextBox.VScroll += (_, _) => UpdateScrollState();
            _licenseTextBox.MouseWheel += (_, _) => UpdateScrollState();
            _licenseTextBox.KeyUp += (_, _) => UpdateScrollState();
            _licenseTextBox.Resize += (_, _) => UpdateScrollState();

            _scrollStatusLabel = new Label
            {
                AutoSize = true,
                Text = "Scroll to the end of the license text to enable Accept."
            };

            _agreeCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = $"I have read and agree to the terms of the {AppIdentity.LicenseName}."
            };
            _agreeCheckBox.CheckedChanged += (_, _) => UpdateAcceptState();

            FlowLayoutPanel buttonPanel = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };

            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Padding = new Padding(12, 5, 12, 5)
            };

            _acceptButton = new Button
            {
                Text = "Accept",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Padding = new Padding(12, 5, 12, 5),
                Enabled = false
            };

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(_acceptButton);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(metadata, 0, 1);
            root.Controls.Add(officialLink, 0, 2);
            root.Controls.Add(_licenseTextBox, 0, 3);
            root.Controls.Add(_scrollStatusLabel, 0, 4);
            root.Controls.Add(_agreeCheckBox, 0, 5);
            root.Controls.Add(buttonPanel, 0, 6);

            Controls.Add(root);

            AcceptButton = _acceptButton;
            CancelButton = cancelButton;
            Shown += (_, _) => UpdateScrollState();
        }

        private void UpdateScrollState()
        {
            if (_licenseTextBox.TextLength == 0)
            {
                _didReachBottom = true;
            }
            else
            {
                int bottomIndex = _licenseTextBox.GetCharIndexFromPosition(
                    new Point(Math.Max(1, _licenseTextBox.ClientSize.Width - 8), Math.Max(1, _licenseTextBox.ClientSize.Height - 8)));
                _didReachBottom = bottomIndex >= (_licenseTextBox.TextLength - 2);
            }

            _scrollStatusLabel.Text = _didReachBottom
                ? "End reached. Check the agreement box to enable Accept."
                : "Scroll to the end of the license text to enable Accept.";

            UpdateAcceptState();
        }

        private void UpdateAcceptState()
        {
            _acceptButton.Enabled = _didReachBottom && _agreeCheckBox.Checked;
        }

        private void OpenOfficialLicense()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppIdentity.OfficialLicenseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open link:{Environment.NewLine}{AppIdentity.OfficialLicenseUrl}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    InstallerTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
