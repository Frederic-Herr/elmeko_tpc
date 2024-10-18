using SFB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NPOI;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.SS.Util;
using NPOI.HSSF.Util;
using I2.Loc;
using System.Diagnostics;
using System.ComponentModel;

public class Record : MonoBehaviour
{
    public class ActiveRecording
    {
        public SerialPortUtility.SerialPortUtilityPro Port;
        public int RepeatCount;
        public int TotalRepeatedAmount;
        public float RunningTime;
        public float IntervalTime;
        public bool IsSDCard;
        public float Interval = 10f;
        public float Duration = 3600f;
        public int Iteration;
        public int ErrorRow;
        public DateTime StartTime;
        public RecordingEntry ListEntry;
        public string SaveLocation;
        public bool SDCardIsMissing;
        public string StartTimeString => $"{StartTime.Year}-{StartTime.Day.ToString("00")}-{StartTime.Month.ToString("00")}_{StartTime.Hour.ToString("00")}-{StartTime.Minute.ToString("00")}-{StartTime.Second.ToString("00")}";
    }

    [SerializeField] private TMP_InputField repeatCountInputField;
    [SerializeField] private GameObject recordingPrefab;
    [SerializeField] private Transform recordingParent;
    [SerializeField] private TMP_Text pathText;
    [SerializeField] private Slider recordDurationSlider;
    [SerializeField] private Slider recordIntervalSlider;
    [SerializeField] private Slider recordDurationSliderSDCard;
    [SerializeField] private Slider recordIntervalSliderSDCard;
    [SerializeField] private Toggle sdCardToggle;
    [SerializeField] private Toggle pcToggle;
    [SerializeField] private Button savePathButton;
    [SerializeField] private Button startRecordingButton;
    [SerializeField] private Button stopRecordingButton;
    [SerializeField] private Button startRecordingPCButton;
    [SerializeField] private Button stopRecordingPCButton;
    [SerializeField] private GameObject sdCardBlocker;
    [SerializeField] private GameObject pcBlocker;
    [SerializeField] private RecordValues tpcRecordValues;
    [SerializeField] private RecordValues hpcRecordValues;

    private bool saveOnPC;
    public bool SaveOnPC
    {
        get { return saveOnPC; }
        set
        {
            saveOnPC = value;
        }
    }

    private bool recording;
    public bool Recording
    {
        get { return recording; }
        set
        {
            if (recording == value) return;

            recording = value;

            currentRecordTime = 0f;

            if (recording)
            {
                if (XPCReader.Instance.CurrentSerialPort == null)
                {
                    recording = false;
                    return;
                }

                XPCReader.Instance.AddUpdateString("ZPLJ", "1.00");
                AddActiveRecord(reader.CurrentSerialPort, true);
            }
            else
            {
                XPCReader.Instance.AddUpdateString("ZPLJ", "0.00");
                RemoveActiveRecord(reader.CurrentSerialPort, true);
            }

            startRecordingButton.gameObject.SetActive(!recording);
            stopRecordingButton.gameObject.SetActive(recording);
            sdCardBlocker.SetActive(recording);
        }
    }

    private bool inTransmission;
    public bool InTransmission
    {
        get { return inTransmission; }
        set
        {
            if (inTransmission == value) return;

            inTransmission = value;

            if (!reader.CurrentSerialPort)
            {
                inTransmission = false;
                return;
            }

            if (inTransmission)
            {
                AddActiveRecord(reader.CurrentSerialPort, false);
            }
            else
            {
                RemoveActiveRecord(reader.CurrentSerialPort, false);
            }

            savePathButton.interactable = !inTransmission;
            startRecordingPCButton.gameObject.SetActive(!inTransmission);
            stopRecordingPCButton.gameObject.SetActive(inTransmission);
            pcBlocker.SetActive(inTransmission);
        }
    }

    private string savePath;
    public string SavePath
    {
        get { return savePath; }
        set
        {
            savePath = value;
            pathText?.SetText(savePath);
            PlayerPrefs.SetString("RecordPath", SavePath);
        }
    }

    private float recordDuration = 3600f;
    private float recordInterval = 10f;

    private float currentRecordTime;
    private float currentTransmissionTime;

    private string forceTransmission;
    private string forceRecording;

    private ErrorHandler errorHandler;
    private List<ErrorHandler.TPCError> errors = new List<ErrorHandler.TPCError>();

    private XPCReader reader;

    public static List<ActiveRecording> activeRecordings = new List<ActiveRecording>();

    /// <summary>
    /// Called when the script is initialized.
    /// It sets the default path for saving recordings to the user's documents folder.
    /// It also sets up the value changed listeners for the record duration and interval sliders.
    /// Additionally, it sets up the value changed listener for the "Save on PC" toggle and starts the HandleTransmissions coroutine.
    /// </summary>
    private void Awake()
    {
        SavePath = PlayerPrefs.GetString("RecordPath", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        recordDurationSlider?.onValueChanged.AddListener(x => recordDuration = x * 3600f);
        recordIntervalSlider?.onValueChanged.AddListener(x => recordInterval = x);

        pcToggle.onValueChanged.AddListener(x => SaveOnPC = x);

        XPCReader.OnJSONReceived += XPCReader_OnJSONReceived;

        StartCoroutine(HandleTransmissions());
    }

    private void Start()
    {
        reader = XPCReader.Instance;
        errorHandler = ErrorHandler.Instance;
        ErrorHandler.OnErrorAdded += ErrorHandler_OnErrorAdded;
    }

    private void ErrorHandler_OnErrorAdded(ErrorHandler.TPCError obj)
    {
        errors.Add(obj);
    }

    /// <summary>
    /// Called when the XPCReader receives a JSON update.
    /// It checks if the controller is currently recording to the SD card.
    /// If the controller is recording, it adds a new active recording.
    /// If the controller is not recording, it removes an active recording.
    /// </summary>
    /// <param name="sender">The serial port that sent the update.</param>
    /// <param name="arg2">The dictionary of values in the JSON update.</param>
    private void XPCReader_OnJSONReceived(SerialPortUtility.SerialPortUtilityPro sender, Dictionary<string, string> arg2)
    {
        string recordingSDCardString = "0.00";

        if (arg2.TryGetValue("ZPLJ", out recordingSDCardString))
        {
            if (reader.CurrentSerialPort == null || reader.CurrentSerialPort != sender)
            {
                if (recordingSDCardString == "1.00")
                {
                    AddActiveRecord(sender, true);
                }
                else
                {
                    RemoveActiveRecord(sender, true);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(forceRecording) && forceRecording != recordingSDCardString) return;

                if (!Recording && recordingSDCardString == "1.00")
                {
                    Recording = true;
                }
                else if (Recording && recordingSDCardString == "0.00")
                {
                    Recording = false;
                }
            }
        }

        for (int i = 0; i < activeRecordings.Count; i++)
        {
            if (activeRecordings[i].Port == sender && activeRecordings[i].IsSDCard)
            {
                string sdCardErrorString = XPCReader.GetValueByName(activeRecordings[i].Port, "SDNM", "0.00");

                activeRecordings[i].SDCardIsMissing = sdCardErrorString == "1.00";

                if (activeRecordings[i].SDCardIsMissing)
                {
                    activeRecordings[i].IntervalTime = 0f;
                }
            }
        }
    }

    private void Update()
    {
        HandleActiveRecordings();
    }

    /// <summary>
    /// Handles the active recordings.
    /// It goes through all active recordings and checks if they have reached their end time.
    /// If a recording has reached its end time, it checks if the recording should be repeated.
    /// If the recording should not be repeated, it stops the transmission and removes the recording from the list.
    /// If the recording should be repeated, it resets the running time and interval time of the recording.
    /// </summary>
    private void HandleActiveRecordings()
    {
        if (activeRecordings == null || activeRecordings.Count == 0) return;

        List<ActiveRecording> recordsToEnd = new List<ActiveRecording>();

        foreach (var record in activeRecordings)
        {
            if (record.SDCardIsMissing) continue;

            record.RunningTime += Time.deltaTime;
            record.IntervalTime += Time.deltaTime;

            if (!record.IsSDCard && record.RunningTime >= record.Duration)
            {
                recordsToEnd.Add(record);
            }
        }

        foreach (var recordToEnd in recordsToEnd)
        {
            if (recordToEnd.RepeatCount == 0 || (recordToEnd.RepeatCount > 0 && recordToEnd.TotalRepeatedAmount < recordToEnd.RepeatCount - 1))
            {
                recordToEnd.RunningTime = 0f;
                recordToEnd.IntervalTime = 0f;
                recordToEnd.TotalRepeatedAmount++;
                recordToEnd.ListEntry?.UpdateRepetitionText();
                continue;
            }

            if (recordToEnd.Port == reader.CurrentSerialPort)
            {
                StopTransmission();
            }
            else
            {
                RemoveActiveRecord(recordToEnd.Port, recordToEnd.IsSDCard);
            }
        }
    }

    public void StartRecording()
    {
        Recording = true;
        forceRecording = Recording ? "1.00" : "0.00";
    }

    public void StopRecording()
    {
        Recording = false;
        forceRecording = Recording ? "1.00" : "0.00";
    }

    public void StartTransmission()
    {
        InTransmission = true;
        forceTransmission = InTransmission ? "1.00" : "0.00";
    }

    public void StopTransmission()
    {
        InTransmission = false;
        forceTransmission = InTransmission ? "1.00" : "0.00";
    }

    /// <summary>
    /// Adds a recording to the list of active recordings.
    /// The recording is only added if there is no other recording with the same port and isSDCard value.
    /// The duration and interval of the recording are set based on the current values of the duration and interval sliders.
    /// The repeat count is set based on the current value of the repeat count input field.
    /// A new RecordingEntry is instantiated and set up with the active recording.
    /// The RecordingEntry is then added to the recording parent and the active recording is added to the list of active recordings.
    /// </summary>
    /// <param name="port">The port of the recording.</param>
    /// <param name="isSDCard">A value indicating whether the recording is an SD card recording.</param>
    private void AddActiveRecord(SerialPortUtility.SerialPortUtilityPro port, bool isSDCard)
    {
        if (activeRecordings.Any(x => x.Port == port && x.IsSDCard == isSDCard))
        {
            return;
        }

        float duration = isSDCard ? (recordDurationSliderSDCard.value * 0.5f) : recordDurationSlider.value;
        float interval = isSDCard ? recordIntervalSliderSDCard.value : recordIntervalSlider.value;

        int repeatCount = 0;
        try
        {
            repeatCount = Convert.ToInt32(repeatCountInputField.text);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log(ex.Message);
            repeatCount = 0;
        }

        if (repeatCount < 0) repeatCount = 1;

        ActiveRecording activeRecording = new ActiveRecording();
        activeRecording.Port = port;
        activeRecording.IsSDCard = isSDCard;
        activeRecording.Duration = duration * 3600f;
        activeRecording.RepeatCount = repeatCount;
        activeRecording.Interval = interval;
        activeRecording.StartTime = DateTime.Now;
        activeRecording.SaveLocation = SavePath;
        activeRecordings.Add(activeRecording);

        GameObject recordEntryObj = Instantiate(recordingPrefab, recordingParent);
        RecordingEntry recordingEntry = recordEntryObj.GetComponent<RecordingEntry>();
        recordingEntry.SetUpEntry(activeRecording);

        activeRecording.ListEntry = recordingEntry;
    }

    /// <summary>
    /// Removes a recording from the list of active recordings.
    /// It removes the recording based on the port and isSDCard value.
    /// It also removes the recording entry from the recording parent.
    /// </summary>
    /// <param name="port">The port of the recording.</param>
    /// <param name="isSDCard">A value indicating whether the recording is an SD card recording.</param>
    private void RemoveActiveRecord(SerialPortUtility.SerialPortUtilityPro port, bool isSDCard)
    {
        ActiveRecording activeRecording = null;
        try
        {
            activeRecording = activeRecordings.Find(x => x.Port == reader.CurrentSerialPort && x.IsSDCard == isSDCard);

            if (activeRecording == null) return;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError(ex.Message);
            return;
        }

        Destroy(activeRecording.ListEntry.gameObject);
        activeRecordings.Remove(activeRecording);
    }

    /// <summary>
    /// Handles the transmissions of all active recordings.
    /// It goes through all active recordings and checks if the interval time has reached the interval.
    /// If the interval time has reached the interval, it either saves the recording to the SD card
    /// or calls the OnSaved method of the recording entry.
    /// </summary>
    private IEnumerator HandleTransmissions()
    {
        StringBuilder output = new StringBuilder();

        while (true)
        {
            foreach (var record in activeRecordings)
            {
                if (record.IntervalTime >= record.Interval)
                {
                    if (record.IsSDCard)
                    {
                        record.IntervalTime = 0f;
                        record.ListEntry?.CallOnSaved();
                    }
                    else
                    {
                        SaveRecording(record);
                    }
                }
            }
            yield return null;
        }
    }

    /// <summary>
    /// Opens a folder selection dialog and sets the save path to the selected folder.
    /// </summary>
    public void SetSavePath()
    {
        var paths = StandaloneFileBrowser.OpenFolderPanel("Save Path", SavePath, false);

        if (paths != null && paths.Length > 0)
        {
            SavePath = paths[0];
        }
    }

    /// <summary>
    /// Opens the folder specified by the SavePath property in the default file explorer.
    /// </summary>
    public void OpenSavePath()
    {
        try
        {
            Process.Start(SavePath);
        }
        catch (Win32Exception win32Exception)
        {
            Console.WriteLine(win32Exception.Message);
        }
    }

    /// <summary>
    /// Saves the given ActiveRecording to an excel file.
    /// </summary>
    /// <param name="record">The ActiveRecording to save.</param>
    private void SaveRecording(ActiveRecording record)
    {
        IWorkbook workbook = new XSSFWorkbook();
        ISheet valuesSheet = workbook.CreateSheet("Values");
        ISheet errorSheet = workbook.CreateSheet("Errors");

        string fileName = $"{XPCReader.GetComPortName(record.Port)}_{record.StartTimeString}.xlsx";
        var newFile = $"{savePath}/{fileName}";

        if (File.Exists(newFile))
        {
            using (var fs = new FileStream(newFile, FileMode.Open, FileAccess.Read))
            {
                workbook = new XSSFWorkbook(fs);
                if (workbook == null) workbook = new XSSFWorkbook();

                valuesSheet = workbook.GetSheet("Values");
                if (valuesSheet == null) valuesSheet = workbook.CreateSheet("Values");

                errorSheet = workbook.GetSheet("Errors");
                if (errorSheet == null) errorSheet = workbook.CreateSheet("Errors");
            }
        }

        using (var fs = new FileStream(newFile, FileMode.Create, FileAccess.Write))
        {
            RecordValues recordValues = XPCReader.GetControllerType(record.Port) == ControllerType.TPC300 ? tpcRecordValues : hpcRecordValues;
            ValueToSave[] valuesToSave;
            string[] headers;
            List<string> keyList = new List<string>();

            valuesToSave = recordValues.ValuesToSave;
            headers = new string[recordValues.ValuesToSave.Length + 1];
            keyList = new List<string>(recordValues.ValuesToSave.Select(x => x.JSONKey));

            for (int i = 1; i < headers.Length; i++)
            {
                string headerText = recordValues.ValuesToSave[i - 1].HeaderKey;
                if (LocalizationManager.TryGetTranslation("Recording/" + recordValues.ValuesToSave[i - 1].HeaderKey, out string translatedHeaderText))
                {
                    headerText = translatedHeaderText;
                }
                headers[i] = $"{headerText} ({valuesToSave[i - 1].JSONKey})";
            }

            string timeHeaderText = "Time";
            if (LocalizationManager.TryGetTranslation("Time", out string translatedTimeHeaderText))
            {
                timeHeaderText = translatedTimeHeaderText;
            }

            headers[0] = timeHeaderText;

            if (record.Iteration == 0)
            {
                var style = workbook.CreateCellStyle();
                style.FillForegroundColor = HSSFColor.Red.Index;
                style.FillPattern = FillPattern.SolidForeground;
                IFont font = workbook.CreateFont();
                font.Color = HSSFColor.White.Index;
                font.IsBold = true;
                style.SetFont(font);

                IRow headerRow = valuesSheet.CreateRow(0);
                for (int i = 0; i < headers.Length; i++)
                {
                    ICell cell = headerRow.CreateCell(i);
                    cell.CellStyle = style;
                    cell.SetCellValue(headers[i]);
                    valuesSheet.AutoSizeColumn(i);
                }
                record.Iteration++;
            }

            string[] values = new string[headers.Length];

            record.IntervalTime = 0f;

            values[0] = DateTime.Now.ToString();
            IRow valueRow = valuesSheet.CreateRow(record.Iteration);
            valueRow.CreateCell(0).SetCellValue(values[0]);
            valuesSheet.AutoSizeColumn(0);

            for (int i = 0; i < recordValues.ValuesToSave.Length; i++)
            {
                string value = XPCReader.GetValueByName(record.Port, valuesToSave[i].JSONKey, "-");
                if (recordValues.ValuesToSave[i].HasRequirement)
                {
                    string requiredValue = XPCReader.GetValueByName(record.Port, recordValues.ValuesToSave[i].Requirement.JSONKey, "");

                    bool check1 = recordValues.ValuesToSave[i].Requirement.CheckEqual && requiredValue != recordValues.ValuesToSave[i].Requirement.Value;
                    bool check2 = !recordValues.ValuesToSave[i].Requirement.CheckEqual && requiredValue == recordValues.ValuesToSave[i].Requirement.Value;

                    if (check1 || check2)
                    {
                        string alternateText = recordValues.ValuesToSave[i].Requirement.AlternateKey;
                        if (LocalizationManager.TryGetTranslation("Recording/" + recordValues.ValuesToSave[i].Requirement.AlternateKey, out alternateText))
                        {
                            values[i + 1] = alternateText;
                        }
                        else
                        {
                            values[i + 1] = recordValues.ValuesToSave[i].Requirement.AlternateKey;
                        }
                    }
                    else
                    {
                        values[i + 1] = value + valuesToSave[i].Suffix;
                    }
                }
                else
                {
                    if (valuesToSave[i].JSONKey == "SYST")
                    {
                        value = Dashboard.GetDeviceModeString(record.Port, true);
                    }
                    else if (valuesToSave[i].JSONKey == "CNTR")
                    {
                        if (DeviceManager.Instance.CurrentDevice)
                        {
                            value = DeviceManager.Instance.CurrentDevice.RelatedDevice.DeviceName;
                        }
                        else
                        {
                            value = "-";
                        }
                    }
                    else if (valuesToSave[i].JSONKey == "REGT")
                    {
                        value = Regulation.GetRegulation(record.Port);
                    }
                    else if (valuesToSave[i].JSONKey == "CurrentOverall")
                    {
                        value = XPCReader.GetCurrent(record.Port).ToString("0.0");
                    }

                    values[i + 1] = value + valuesToSave[i].Suffix;
                }

                valueRow.CreateCell(i + 1).SetCellValue(values[i + 1]);
                valuesSheet.AutoSizeColumn(i + 1);
            }

            timeHeaderText = headers[0];
            headers = new string[3];
            headers[0] = timeHeaderText;

            string errorCodeHeader = "Error Code";
            if (LocalizationManager.TryGetTranslation("Recording/ErrorCode", out string translatedErrorCodeHeaderText))
            {
                errorCodeHeader = translatedErrorCodeHeaderText;
            }

            headers[1] = errorCodeHeader;

            string errorDescriptionHeader = "Description";
            if (LocalizationManager.TryGetTranslation("Recording/ErrorDescription", out string translatedErrorDescriptionHeaderText))
            {
                errorDescriptionHeader = translatedErrorDescriptionHeaderText;
            }

            headers[2] = errorDescriptionHeader;

            if (record.Iteration == 1)
            {
                var style = workbook.CreateCellStyle();
                style.FillForegroundColor = HSSFColor.Red.Index;
                style.FillPattern = FillPattern.SolidForeground;
                IFont font = workbook.CreateFont();
                font.Color = HSSFColor.White.Index;
                font.IsBold = true;
                style.SetFont(font);

                IRow headerRow = errorSheet.CreateRow(0);
                for (int i = 0; i < headers.Length; i++)
                {
                    ICell cell = headerRow.CreateCell(i);
                    cell.CellStyle = style;
                    cell.SetCellValue(headers[i]);
                    valuesSheet.AutoSizeColumn(i);
                }

                record.ErrorRow++;
            }

            if (errors.Count > 0)
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    IRow errorRow = errorSheet.CreateRow(record.ErrorRow);
                    errorRow.CreateCell(0).SetCellValue(values[0]);
                    errorSheet.AutoSizeColumn(0);

                    errorRow.CreateCell(1).SetCellValue(errors[i].controllerErrorCode);
                    errorSheet.AutoSizeColumn(1);

                    string errorString = errors[i].languageKey;
                    if (LocalizationManager.TryGetTranslation(errorString, out string translatedErrorText))
                    {
                        errorString = translatedErrorText;
                    }
                    errorRow.CreateCell(2).SetCellValue(errorString);
                    errorSheet.AutoSizeColumn(2);

                    record.ErrorRow++;
                }

                IRow emptyRow = errorSheet.CreateRow(record.ErrorRow);
                record.ErrorRow++;
            }

            errors.Clear();

            workbook.Write(fs);

            record.Iteration++;

            record.ListEntry?.CallOnSaved();
        }
    }
}
