#nullable enable
namespace PersonalMemoirsReader
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer? components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }
        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            labelProfile = new Label(); textProfile = new TextBox(); buttonRecord = new Button(); buttonStop = new Button();
            buttonImport = new Button(); listSamples = new ListBox(); labelSample = new Label(); labelSelected = new Label();
            buttonListen = new Button(); buttonStopListen = new Button(); buttonDelete = new Button(); labelText = new Label(); textMemoir = new TextBox();
            buttonOpenText = new Button(); buttonSaveText = new Button(); labelStatus = new Label(); buttonGenerate = new Button();
            labelFfmpeg = new Label(); textFfmpeg = new TextBox(); buttonFfmpeg = new Button();
            labelEngine = new Label(); comboVoices = new ComboBox(); buttonStopSpeech = new Button();
            buttonChatterbox = new Button(); buttonCancelChatterbox = new Button(); buttonAiLog = new Button();
            labelQuality = new Label(); comboQuality = new ComboBox(); buttonHistory = new Button();
            buttonF5 = new Button(); buttonF5Setup = new Button(); labelReferenceText = new Label(); textReference = new TextBox();
            labelF5Speed = new Label(); numericF5Speed = new NumericUpDown(); labelF5Hint = new Label(); buttonVoiceColor = new Button();
            SuspendLayout();
            labelProfile.AutoSize = true; labelProfile.Location = new Point(16, 14); labelProfile.Name = "labelProfile"; labelProfile.Text = "Voice profile:";
            textProfile.Location = new Point(115, 10); textProfile.Name = "textProfile"; textProfile.Size = new Size(255, 23); textProfile.Text = "My Voice"; textProfile.Leave += textProfile_Leave;
            buttonRecord.Location = new Point(388, 8); buttonRecord.Name = "buttonRecord"; buttonRecord.Size = new Size(96, 28); buttonRecord.Text = "Record"; buttonRecord.Click += buttonRecord_Click;
            buttonStop.Location = new Point(490, 8); buttonStop.Name = "buttonStop"; buttonStop.Size = new Size(75, 28); buttonStop.Text = "Stop"; buttonStop.Enabled = false; buttonStop.Click += buttonStop_Click;
            buttonImport.Location = new Point(571, 8); buttonImport.Name = "buttonImport"; buttonImport.Size = new Size(156, 28); buttonImport.Text = "Import Audio..."; buttonImport.Click += buttonImport_Click;
            labelSample.AutoSize = true; labelSample.Location = new Point(16, 49); labelSample.Name = "labelSample"; labelSample.Text = "Voice samples (select one):";
            listSamples.Location = new Point(16, 70); listSamples.Name = "listSamples"; listSamples.Size = new Size(550, 89); listSamples.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; listSamples.HorizontalScrollbar = true; listSamples.SelectedIndexChanged += listSamples_SelectedIndexChanged;
            buttonListen.Location = new Point(581, 70); buttonListen.Name = "buttonListen"; buttonListen.Size = new Size(146, 28); buttonListen.Text = "Play Selected"; buttonListen.Click += buttonListen_Click;
            buttonStopListen.Location = new Point(581, 104); buttonStopListen.Name = "buttonStopListen"; buttonStopListen.Size = new Size(146, 28); buttonStopListen.Text = "Stop Playback"; buttonStopListen.Enabled = false; buttonStopListen.Click += buttonStopListen_Click;
            buttonDelete.Location = new Point(581, 138); buttonDelete.Name = "buttonDelete"; buttonDelete.Size = new Size(146, 28); buttonDelete.Text = "Delete Selected"; buttonDelete.Click += buttonDelete_Click;
            labelSelected.AutoSize = true; labelSelected.Location = new Point(16, 171); labelSelected.Name = "labelSelected"; labelSelected.Text = "Selected: none";
            labelFfmpeg.AutoSize = true; labelFfmpeg.Location = new Point(16, 200); labelFfmpeg.Name = "labelFfmpeg"; labelFfmpeg.Text = "FFmpeg (optional):";
            textFfmpeg.Location = new Point(145, 196); textFfmpeg.Name = "textFfmpeg"; textFfmpeg.Size = new Size(420, 23); textFfmpeg.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            buttonFfmpeg.Location = new Point(581, 195); buttonFfmpeg.Name = "buttonFfmpeg"; buttonFfmpeg.Size = new Size(146, 26); buttonFfmpeg.Text = "Browse..."; buttonFfmpeg.Click += buttonFfmpeg_Click;
            labelText.AutoSize = true; labelText.Location = new Point(16, 238); labelText.Name = "labelText"; labelText.Text = "Memoir text (editable):";
            buttonOpenText.Location = new Point(480, 230); buttonOpenText.Name = "buttonOpenText"; buttonOpenText.Size = new Size(118, 28); buttonOpenText.Text = "Open TXT..."; buttonOpenText.Click += buttonOpenText_Click;
            buttonSaveText.Location = new Point(609, 230); buttonSaveText.Name = "buttonSaveText"; buttonSaveText.Size = new Size(118, 28); buttonSaveText.Text = "Save TXT..."; buttonSaveText.Click += buttonSaveText_Click;
            textMemoir.Location = new Point(16, 267); textMemoir.Name = "textMemoir"; textMemoir.Size = new Size(711, 96); textMemoir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; textMemoir.Multiline = true; textMemoir.ScrollBars = ScrollBars.Vertical; textMemoir.Font = new Font("Segoe UI", 11F); textMemoir.Text = "Ovdje upišite tekst svojih sjećanja."; textMemoir.WordWrap = true; textMemoir.MaxLength = 0;
            labelEngine.AutoSize = true; labelEngine.Location = new Point(16, 375); labelEngine.Name = "labelEngine"; labelEngine.Anchor = AnchorStyles.Top | AnchorStyles.Left; labelEngine.Text = "Windows voice:";
            comboVoices.Location = new Point(145, 371); comboVoices.Name = "comboVoices"; comboVoices.Size = new Size(420, 23); comboVoices.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; comboVoices.DropDownStyle = ComboBoxStyle.DropDownList;
            buttonStopSpeech.Location = new Point(581, 370); buttonStopSpeech.Name = "buttonStopSpeech"; buttonStopSpeech.Size = new Size(146, 26); buttonStopSpeech.Anchor = AnchorStyles.Top | AnchorStyles.Right; buttonStopSpeech.Text = "Stop Reading"; buttonStopSpeech.Click += buttonStopSpeech_Click;
            buttonGenerate.Location = new Point(16, 408); buttonGenerate.Name = "buttonGenerate"; buttonGenerate.Size = new Size(200, 32); buttonGenerate.Anchor = AnchorStyles.Top | AnchorStyles.Left; buttonGenerate.Text = "Read Text Aloud"; buttonGenerate.Click += buttonGenerate_Click;
            labelStatus.Location = new Point(232, 408); labelStatus.Name = "labelStatus"; labelStatus.Size = new Size(495, 45); labelStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; labelStatus.Text = "Initializing Windows offline speech...";
            labelQuality.Location = new Point(16, 583); labelQuality.AutoSize = true; labelQuality.Text = "Voice style:";
            comboQuality.Location = new Point(105, 580); comboQuality.Size = new Size(310, 23); comboQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            comboQuality.Items.AddRange(new object[] {
                "Original baseline (exag 0.5 / CFG 0.5)"
            }); comboQuality.SelectedIndex = 0;
            buttonHistory.Location = new Point(551, 459); buttonHistory.Size = new Size(176, 34); buttonHistory.Text = "Compare WAV Files"; buttonHistory.Click += buttonHistory_Click;
            labelReferenceText.Location = new Point(16, 512); labelReferenceText.AutoSize = true; labelReferenceText.Text = "Reference transcript (F5):";
            textReference.Location = new Point(190, 509); textReference.Size = new Size(537, 23); textReference.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textReference.PlaceholderText = "Exact words spoken in the reference excerpt";
            labelF5Speed.Location = new Point(404, 551); labelF5Speed.AutoSize = true; labelF5Speed.Text = "F5 speaking speed:";
            numericF5Speed.Location = new Point(538, 547); numericF5Speed.Size = new Size(90, 23); numericF5Speed.DecimalPlaces = 2; numericF5Speed.Increment = 0.05M; numericF5Speed.Minimum = 0.70M; numericF5Speed.Maximum = 1.20M; numericF5Speed.Value = 0.90M;
            labelF5Hint.Location = new Point(16, 657); labelF5Hint.Size = new Size(711, 42); labelF5Hint.Text = "1.00 = original F5 speed. Lower = slower narration.\nTranscript must match the chosen excerpt exactly.";
            buttonF5Setup.Location = new Point(16, 545); buttonF5Setup.Size = new Size(150, 32); buttonF5Setup.Text = "Install / Verify F5"; buttonF5Setup.Click += buttonF5Setup_Click;
            buttonF5.Location = new Point(177, 545); buttonF5.Size = new Size(210, 32); buttonF5.Text = "Generate with F5-TTS"; buttonF5.Click += buttonF5_Click;
            buttonChatterbox.Location = new Point(16, 459); buttonChatterbox.Name = "buttonChatterbox"; buttonChatterbox.Size = new Size(245, 34); buttonChatterbox.Anchor = AnchorStyles.Top | AnchorStyles.Left; buttonChatterbox.Text = "Generate with Chatterbox AI"; buttonChatterbox.Click += buttonChatterbox_Click;
            buttonCancelChatterbox.Location = new Point(273, 459); buttonCancelChatterbox.Name = "buttonCancelChatterbox"; buttonCancelChatterbox.Size = new Size(155, 34); buttonCancelChatterbox.Anchor = AnchorStyles.Top | AnchorStyles.Left; buttonCancelChatterbox.Text = "Stop AI / Playback"; buttonCancelChatterbox.Enabled = false; buttonCancelChatterbox.Click += buttonCancelChatterbox_Click;
            AutoScaleDimensions = new SizeF(7F, 15F); AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(745, 710); MinimumSize = new Size(760, 750);
            buttonAiLog.Location = new Point(440, 459); buttonAiLog.Name = "buttonAiLog"; buttonAiLog.Size = new Size(100, 34); buttonAiLog.Anchor = AnchorStyles.Top | AnchorStyles.Left; buttonAiLog.Text = "AI Log"; buttonAiLog.Click += buttonAiLog_Click;
            Controls.Add(buttonHistory);
            Controls.Add(labelReferenceText); Controls.Add(textReference); Controls.Add(buttonF5Setup); Controls.Add(buttonF5);
            buttonVoiceColor.Location = new Point(405, 693); buttonVoiceColor.Size = new Size(322, 32); buttonVoiceColor.Text = "Test Voice Color (experimental)..."; buttonVoiceColor.Click += buttonVoiceColor_Click;
            
            Controls.Add(labelF5Speed); Controls.Add(numericF5Speed); Controls.Add(labelF5Hint);
            InitializeReferenceControls();
            Controls.Add(buttonChatterbox); Controls.Add(buttonCancelChatterbox); Controls.Add(buttonAiLog);
            Controls.Add(labelProfile); Controls.Add(textProfile); Controls.Add(buttonRecord); Controls.Add(buttonStop);
            Controls.Add(buttonImport); Controls.Add(listSamples); Controls.Add(labelSample); Controls.Add(labelSelected);
            Controls.Add(buttonListen); Controls.Add(buttonStopListen); Controls.Add(buttonDelete); Controls.Add(labelText); Controls.Add(textMemoir);
            Controls.Add(buttonOpenText); Controls.Add(buttonSaveText); Controls.Add(labelStatus); Controls.Add(buttonGenerate);
            Controls.Add(labelFfmpeg); Controls.Add(textFfmpeg); Controls.Add(buttonFfmpeg);
            Controls.Add(labelEngine); Controls.Add(comboVoices); Controls.Add(buttonStopSpeech);
            Name = "MainForm"; StartPosition = FormStartPosition.CenterScreen; Text = "Personal Memoirs Reader v0.1.31";
            ResumeLayout(false); PerformLayout();
        }
        #endregion
        private Label labelProfile = null!, labelSample = null!, labelSelected = null!, labelText = null!, labelStatus = null!, labelFfmpeg = null!, labelEngine = null!;
        private TextBox textProfile = null!, textMemoir = null!, textFfmpeg = null!;
        private ComboBox comboVoices = null!, comboQuality = null!;
        private Label labelQuality = null!, labelReferenceText = null!;
        private TextBox textReference = null!;
        private Button buttonF5 = null!, buttonF5Setup = null!, buttonVoiceColor = null!;
        private Label labelF5Speed = null!, labelF5Hint = null!;
        private NumericUpDown numericF5Speed = null!;
        private Button buttonHistory = null!;
        private Button buttonStopSpeech = null!, buttonChatterbox = null!, buttonCancelChatterbox = null!, buttonAiLog = null!;
        private Button buttonRecord = null!, buttonStop = null!, buttonImport = null!, buttonListen = null!, buttonStopListen = null!, buttonDelete = null!, buttonOpenText = null!, buttonSaveText = null!, buttonGenerate = null!, buttonFfmpeg = null!;
        private ListBox listSamples = null!;
    }
}
