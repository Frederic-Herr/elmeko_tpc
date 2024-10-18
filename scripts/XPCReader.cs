using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.IO.Ports;
using TMPro;
using System.Text;
using System.Runtime.InteropServices;
using SerialPortUtility;
using I2.Loc;
using System.Globalization;
using UnityEngine.Events;

/// <summary>
/// Handles serial connections to TPC Controllers
/// </summary>
public class XPCReader : MonoBehaviour
{
    public class SerialInfo
    {
        public SerialPortUtilityPro serialPort = null;
        public string asciiString;
        public float connectionTimer;
        public ControllerEntry relatedEntry;
        public bool InFlashMode;
        public bool IsConnected;
    }

    public static XPCReader Instance { get; private set; }

    [SerializeField] private CanvasGroupController connectingPopupCanvas;
    [SerializeField] private float updateTime = 5f;
    [SerializeField] private float disonnectAfterTime = 10f;
    [SerializeField] private TMP_Text textField;
    [SerializeField] private GameObject serialPortUtilityPrefab;
    [SerializeField] private Menu settingsMenu;
    [SerializeField] private UnityEvent OnPortConnected;
    [SerializeField] private UnityEvent OnPortDisconnected;

    private string updateString;

    private bool sendUpdates = true;
    public bool SendUpdates
    {
        get => sendUpdates;
        set
        {
            if (sendUpdates == value) return;

            sendUpdates = value;
        }
    }

    private SerialPortUtilityPro currentSerialPort = null;

    public SerialPortUtilityPro CurrentSerialPort
    {
        get => currentSerialPort;
        set
        {
            if (currentSerialPort == value) return;

            string oldPort = currentSerialPort ? currentSerialPort.SerialNumber : "None";
            currentSerialPort = value;

            if (currentSerialPort != null)
            {
                PlayerPrefs.SetString("LastUsedPort", currentSerialPort.SerialNumber);

                OnPortConnected?.Invoke();
                OnPortConnectedEvent?.Invoke(currentSerialPort);
            }
            else
            {
                OnPortDisconnected?.Invoke();
                OnPortDisconnectedEvent?.Invoke();
            }
        }
    }

    public static string CurrentControllerID { get; private set; }
    public static ControllerType CurrentControllerType { get; private set; }

    public static bool CurrentControllerInFlashMode { get; private set; }

    private List<SerialInfo> serialPorts = new List<SerialInfo>();

    public static event Action<SerialPortUtilityPro, Dictionary<string, string>> OnJSONReceived;
    public static event Action<SerialPortUtilityPro, Dictionary<string, string>> OnActiveComportChanged;
    public static event Action<SerialPortUtilityPro, Dictionary<string, string>> OnNextJSON;
    public static event Action<SerialPortUtilityPro> OnPortConnectedEvent;
    public static event Action OnPortDisconnectedEvent;

    private Dictionary<string, string> updateDictionary = new Dictionary<string, string>();

    public static Dictionary<string, string> CurrentValues = new Dictionary<string, string>();
    public static Dictionary<SerialPortUtilityPro, Dictionary<string, string>> ActivePorts = new Dictionary<SerialPortUtilityPro, Dictionary<string, string>>();

    [DllImport("spap", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    static extern int spapDeviceListAvailable();
    [DllImport("spap", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    static extern int spapDeviceList(int deviceNum, [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder deviceInfo, int buffer_size);

    private int deviceNum;
    private System.Text.StringBuilder[] deviceString;
    private int[] deviceKind;
    private SerialPortUtilityPro portToChangeTo;
    private SerialConverter[] serialConverters;

    public static bool CallNextJSON;
    private bool waitForJSON;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        serialConverters = FindObjectsOfType<SerialConverter>();
        StartCoroutine(CheckComPorts());
    }

    void Start()
    {
        StartCoroutine(SendUpdate());
        StartCoroutine(CheckConnections());
    }

    public void SetSerialPortEntry(ControllerEntry controllerEntry)
    {
        for (int i = 0; i < serialPorts.Count; i++)
        {
            if (serialPorts[i].serialPort.SerialNumber == controllerEntry.RelatedComport)
            {
                serialPorts[i].relatedEntry = controllerEntry;
                break;
            }
        }
    }

    private IEnumerator CheckComPorts()
    {
        while (true)
        {
            GetDevices();
            yield return new WaitForSeconds(2f);
        }
    }

    /// <summary>
    /// Retrieves the list of available serial devices and updates the serialPorts list.
    /// It checks for device overlaps and sets up new SerialInfo objects for each unique device.
    /// The method utilizes native calls to spapDeviceListAvailable and spapDeviceList to gather device information.
    /// </summary>
    private void GetDevices()
    {
        deviceNum = spapDeviceListAvailable();
        deviceString = new System.Text.StringBuilder[deviceNum];
        deviceKind = new int[deviceNum];
        for (int i = 0; i < deviceNum; i++)
        {
            deviceString[i] = new System.Text.StringBuilder(1024);
            deviceKind[i] = spapDeviceList(i, deviceString[i], 1024);
        }

        foreach (System.Text.StringBuilder str1 in deviceString)
        {
            int overlap = -1;
            foreach (System.Text.StringBuilder str2 in deviceString)
            {
                if (str1.ToString().Equals(str2.ToString()))
                    overlap++;
            }
            str1.Append("," + overlap.ToString());
        }

        if (deviceNum > 0)
        {
            for (int i = 0; i < deviceNum; i++)
            {
                string[] datu = deviceString[i].ToString().Split(',');

                if (datu.Length <= 4 || string.IsNullOrWhiteSpace(datu[3])) continue;

                bool alreadyExists = false;

                for (int j = 0; j < serialPorts.Count; j++)
                {
                    if (serialPorts[j].serialPort && serialPorts[j].serialPort.SerialNumber == datu[3])
                    {
                        alreadyExists = true;
                    }
                }

                if (!alreadyExists)
                {
                    SerialInfo serialInfo = new SerialInfo();
                    GameObject serialPortObj = Instantiate(serialPortUtilityPrefab, transform);
                    serialInfo.serialPort = serialPortObj.GetComponent<SerialPortUtilityPro>();
                    if (datu.Length > 1) serialInfo.serialPort.VendorID = datu[0];
                    if (datu.Length > 2) serialInfo.serialPort.ProductID = datu[1];
                    if (datu.Length > 4) serialInfo.serialPort.SerialNumber = datu[3];
                    if (datu.Length > 5) serialInfo.serialPort.Skip = int.Parse(datu[4]);
                    SerialPortUtilityPro serialPortUtility = serialInfo.serialPort;
                    serialPortUtility.ReadCompleteEventObject.AddListener((x) => OnSerialResponse(serialPortUtility, x));

                    serialPorts.Add(serialInfo);

                    string lastUsedPort = PlayerPrefs.GetString("LastUsedPort", "");
                }
            }
        }
    }

    /// <summary>
    /// Checks for serial port connections and updates the relatedEntry and CurrentSerialPort.
    /// If a connection is lost, it shows a connection pop up and logs an error with the ErrorHandler.
    /// </summary>
    private IEnumerator CheckConnections()
    {
        while (true)
        {
            for (int i = 0; i < serialPorts.Count; i++)
            {
                if (serialPorts[i].relatedEntry == null || serialPorts[i].InFlashMode || !serialPorts[i].IsConnected) continue;

                serialPorts[i].connectionTimer += Time.deltaTime;

                if (serialPorts[i].connectionTimer > disonnectAfterTime)
                {
                    serialPorts[i].IsConnected = false;
                    ErrorHandler.Instance.AddTPCError(serialPorts[i].serialPort.SerialNumber, "ConnectionLost");
                    serialPorts[i].relatedEntry.SetConnectedState(false);
                    if (serialPorts[i].serialPort == CurrentSerialPort) ShowConnectionPopUp(serialPorts[i].serialPort);
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Converts the CurrentValues or updateDictionary into a JSON string.
    /// If CurrentValues is not null and contains values, it constructs the JSON string based on the key-value pairs.
    /// If CurrentValues is empty, it constructs the JSON string based on the updateDictionary key-value pairs.
    /// </summary>
    public string ValuesToJson()
    {
        string resultString = "";
        bool isFirstEntry = true;

        resultString += "{";

        if (CurrentValues != null && CurrentValues.Count > 0)
        {
            foreach (var value in CurrentValues)
            {
                string firstChar = isFirstEntry ? "" : ",";

                if (value.Key == "UCID" || value.Key == "FEFT")
                {
                    resultString += $"{firstChar}\"{value.Key}\":\"{value.Value}\"";
                }
                else
                {
                    resultString += $"{firstChar}\"{value.Key}\":{value.Value}";
                }
                isFirstEntry = false;
            }
        }
        else
        {
            foreach (var value in updateDictionary)
            {
                string firstChar = isFirstEntry ? "" : ",";

                if (value.Key == "UCID" || value.Key == "FEFT")
                {
                    resultString += $"{firstChar}\"{value.Key}\":\"{value.Value}\"";
                }
                else
                {
                    resultString += $"{firstChar}\"{value.Key}\":{value.Value}";
                }
                isFirstEntry = false;
            }
        }

        resultString += "}\r\n";

        return resultString;
    }

    /// <summary>
    /// Adds an update string to the update dictionary or updates an existing value.
    /// If the value name is already in the dictionary, it checks if the new value is the same as the current value.
    /// If the new value is the same, it removes the value from the dictionary.
    /// If the new value is different, it updates the value in the dictionary.
    /// If the value name is not in the dictionary, it adds the value to the dictionary.
    /// </summary>
    /// <param name="valueName">The name of the value to add or update.</param>
    /// <param name="valueString">The new value to add or update.</param>
    public void AddUpdateString(string valueName, string valueString)
    {
        string targetValue = GetValueByName(valueName);
        string newValue = valueString.Replace(',', '.');

        if (updateDictionary.ContainsKey(valueName))
        {
            if (!string.IsNullOrWhiteSpace(targetValue))
            {
                if (valueName == "PGTR") Debug.Log(newValue + " -- " + targetValue);

                if (newValue == targetValue)
                {
                    updateDictionary.Remove(valueName);
                    return;
                }
            }
            updateDictionary[valueName] = valueString;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(targetValue))
            {
                if (newValue == targetValue)
                {
                    return;
                }
            }
            updateDictionary.Add(valueName, valueString);
        }
    }

    /// <summary>
    /// Handles the serial port response event.
    /// It checks if the serial port is in flash mode and if it is, it sets the related flag.
    /// It processes the received JSON string and extracts the key-value pairs.
    /// If the serial port is the current serial port, it updates the related properties and shows a pop up if the controller is in flash mode.
    /// It also calls the OnJSONReceived and OnNextJSON events with the extracted key-value pairs.
    /// </summary>
    /// <param name="sender">The serial port that sent the response.</param>
    /// <param name="response">The JSON response string.</param>
    private void OnSerialResponse(SerialPortUtilityPro sender, object response)
    {
        SerialInfo serialInfo = null;
        for (int i = 0; i < serialPorts.Count; i++)
        {
            if (serialPorts[i].serialPort.SerialNumber == sender.SerialNumber) serialInfo = serialPorts[i];
        }

        if (serialInfo == null) return;

        string responseString = response.ToString();

        if (responseString.StartsWith('{'))
        {
            serialInfo.asciiString = "";
        }

        serialInfo.asciiString += response.ToString();

        if (serialInfo.asciiString.Contains('{') && serialInfo.asciiString.Contains('}'))
        {
            serialInfo.InFlashMode = serialInfo.asciiString.Contains("\"flash\":\"rdy\"");

            textField.text += serialInfo.serialPort.SerialNumber + "\n";

            serialInfo.asciiString = serialInfo.asciiString.Replace("{", "");
            serialInfo.asciiString = serialInfo.asciiString.Replace("}", "");

            string[] lines = serialInfo.asciiString.Split(',');
            Dictionary<string, string> newValues = new Dictionary<string, string>();

            foreach (var line in lines)
            {
                string[] pair = line.Trim().Split(':');
                pair[0] = pair[0].Replace("\"", "");
                pair[0] = pair[0].Replace(" ", "");

                if (pair.Length > 1)
                {
                    if (pair[1].StartsWith(" "))
                    {
                        pair[1] = pair[1].Remove(0, 1);
                    }

                    pair[1] = pair[1].Replace("\"", "");
                    pair[1] = pair[1].Replace(" ", "");

                    if (!newValues.ContainsKey(pair[0])) newValues.Add(pair[0], pair[1]);
                }
            }

            if (serialInfo.serialPort == CurrentSerialPort)
            {
                CurrentValues = newValues;
            }

            foreach (var part in newValues)
            {
                textField.text += part.Key + " = " + part.Value + "\n";
            }

            if (portToChangeTo && serialInfo.serialPort.SerialNumber == portToChangeTo.SerialNumber)
            {
                CurrentSerialPort = portToChangeTo;
                portToChangeTo = null;
                connectingPopupCanvas.HideCanvas();

                string ucidString = "";
                if (newValues.TryGetValue("UCID", out string id))
                {
                    ucidString = id;
                }
                ucidString = ucidString.Replace("\"", string.Empty);

                if (!string.IsNullOrWhiteSpace(ucidString))
                {
                    CurrentControllerID = ucidString;
                    CurrentControllerType = ucidString.StartsWith('h') ? ControllerType.HPC100 : ControllerType.TPC300;
                }

                if (CurrentControllerType == ControllerType.HPC100)
                {
                    string flashModeString = "";
                    newValues.TryGetValue("FLSH", out flashModeString);

                    if (!string.IsNullOrWhiteSpace(flashModeString))
                    {
                        CurrentControllerInFlashMode = flashModeString == "1.00";
                    }
                }
                else
                {
                    string flashModeString = "";
                    newValues.TryGetValue("flash", out flashModeString);

                    if (!string.IsNullOrWhiteSpace(flashModeString))
                    {
                        CurrentControllerInFlashMode = flashModeString == "rdy";

                        LocalizationManager.TryGetTranslation("Cancel", out string cancel);
                        if (string.IsNullOrWhiteSpace(cancel))
                        {
                            cancel = "Cancel";
                        }

                        LocalizationManager.TryGetTranslation("Firmware/FlashModeHeader", out string flashModeHeader);
                        if (string.IsNullOrWhiteSpace(flashModeHeader))
                        {
                            flashModeHeader = "Controller is in flash mode";
                        }

                        LocalizationManager.TryGetTranslation("Firmware/FlashModeDescription", out string flashModeDescription);
                        if (string.IsNullOrWhiteSpace(flashModeHeader))
                        {
                            flashModeDescription = "Go to the Settings menu and update the controller firmware";
                        }

                        GenericPopup.Instance.SetButtonTexts("Okay", cancel);
                        GenericPopup.Instance.SetUpPopup(settingsMenu.Show, null, flashModeHeader, flashModeDescription);
                    }
                }

                CurrentValues = newValues;
                OnActiveComportChanged?.Invoke(CurrentSerialPort, newValues);
            }
            else if (serialInfo.serialPort == CurrentSerialPort && serialInfo.connectionTimer >= disonnectAfterTime)
            {
                connectingPopupCanvas.HideCanvas();
            }

            if (ActivePorts.ContainsKey(serialInfo.serialPort))
            {
                ActivePorts[serialInfo.serialPort] = newValues;
            }
            else
            {
                ActivePorts.Add(serialInfo.serialPort, newValues);
            }

            OnJSONReceived?.Invoke(serialInfo.serialPort, newValues);

            if (waitForJSON && serialInfo.serialPort == CurrentSerialPort)
            {
                waitForJSON = false;
                OnNextJSON?.Invoke(serialInfo.serialPort, newValues);
            }

            serialInfo.connectionTimer = 0f;
            serialInfo.relatedEntry?.SetConnectedState(true);
            serialInfo.IsConnected = true;
            ErrorHandler.Instance.RemoveTPCError(serialInfo.serialPort.SerialNumber, "ConnectionLost");
        }
    }

    public void CancelConnection()
    {
        ControllerList.Instance.SelectedEntry = null;
        portToChangeTo = null;
        CurrentSerialPort = null;
        connectingPopupCanvas.HideCanvas();
    }

    /// <summary>
    /// Sends updates to the serial ports at regular intervals.
    /// The method constructs an update string from the update dictionary and writes it to the current serial port if updates are enabled.
    /// If the current serial port is in flash mode, the update is skipped.
    /// After sending the update, it clears the update dictionary and handles JSON call flags.
    /// If updates are enabled but the update string is empty, an empty JSON object is sent instead to recieve an update from the controller.
    /// This coroutine continues indefinitely, sending updates after each specified delay.
    /// </summary>
    private IEnumerator SendUpdate()
    {
        yield return new WaitForSeconds(0.1f);

        while (true)
        {
            updateString = GetUpdateString();

            for (int i = 0; i < serialPorts.Count; i++)
            {
                if (serialPorts[i].InFlashMode) continue;

                if (SendUpdates && serialPorts[i].serialPort == CurrentSerialPort && !string.IsNullOrWhiteSpace(updateString))
                {
                    CurrentSerialPort.Write(updateString);
                    updateDictionary.Clear();

                    if (CallNextJSON)
                    {
                        waitForJSON = true;
                        CallNextJSON = false;
                    }
                }
                else if (SendUpdates)
                {
                    string jsonString = "{}";
                    serialPorts[i].serialPort.Write(jsonString);
                }
            }

            yield return new WaitForSeconds(updateTime);
        }
    }

    /// <summary>
    /// Determines the type of controller based on the UCID value obtained from the specified serial port.
    /// If the UCID value is not null or empty, it checks if it starts with 'h' to determine if it is an HPC100 controller (dehumidifier).
    /// If the UCID value is not present or does not start with 'h', it defaults to a TPC300 controller type.
    /// </summary>
    public static ControllerType GetControllerType(SerialPortUtilityPro port)
    {
        ControllerType controllerType = ControllerType.TPC300;
        string ucidString = GetValueByName(port, "UCID");

        ucidString = ucidString.Replace("\"", string.Empty);

        if (!string.IsNullOrWhiteSpace(ucidString))
        {
            controllerType = ucidString.StartsWith('h') ? ControllerType.HPC100 : ControllerType.TPC300;
        }

        return controllerType;
    }

    /// <summary>
    /// Converts the update dictionary into a JSON string.
    /// If the update dictionary is empty, an empty string is returned.
    /// </summary>
    /// <returns>The JSON string representation of the update dictionary.</returns>
    private string GetUpdateString()
    {
        string resultString = "";
        bool isFirstEntry = true;

        if (updateDictionary.Count <= 0) return resultString;

        resultString += "{";

        foreach (var part in updateDictionary)
        {
            string firstChar = isFirstEntry ? "" : ",";
            resultString += $"{firstChar}\"{part.Key}\":{part.Value}";
            isFirstEntry = false;
        }

        resultString += "}\r\n";

        return resultString;
    }

    /// <summary>
    /// Selects the serial port with the given comport number.
    /// </summary>
    /// <param name="comport">The comport number of the serial port to select.</param>
    public void SelectComport(string comport)
    {
        SerialPortUtilityPro serialPort = null;

        for (int i = 0; i < serialPorts.Count; i++)
        {
            if (serialPorts[i].serialPort.SerialNumber == comport)
            {
                serialPort = serialPorts[i].serialPort;
            }
        }

        if (serialPort != null) SelectComport(serialPort);
    }

    /// <summary>
    /// Retrieves the serial port with the given comport number.
    /// </summary>
    /// <param name="comport">The comport number of the serial port to retrieve.</param>
    /// <returns>The serial port with the given comport number if it exists, otherwise null.</returns>
    public SerialPortUtilityPro GetPortByName(string comport)
    {
        SerialPortUtilityPro serialPort = null;

        for (int i = 0; i < serialPorts.Count; i++)
        {
            if (serialPorts[i].serialPort.SerialNumber == comport)
            {
                serialPort = serialPorts[i].serialPort;
            }
        }

        return serialPort;
    }

    /// <summary>
    /// Selects the serial port with the given comport number.
    /// If the provided serial port is not null and different from the CurrentSerialPort, it sets the portToChangeTo property to the provided serial port.
    /// It then calls the ShowConnectionPopUp method to display the connection pop up for the selected serial port.
    /// </summary>
    public void SelectComport(SerialPortUtilityPro serialPort)
    {
        if (serialPort == null || serialPort == CurrentSerialPort) return;

        portToChangeTo = serialPort;
        ShowConnectionPopUp(serialPort);
    }

    /// <summary>
    /// Shows the connection pop up for the given serial port.
    /// The pop up text is set to "Connect with <serialPortNumber>".
    /// The <serialPortNumber> is retrieved from the PlayerPrefs using the serial port number as the key.
    /// If the PlayerPrefs does not contain the serial port number as a key, the serial port number is used as the default value.
    /// </summary>
    /// <param name="serialPort">The serial port for which to show the connection pop up.</param>
    private void ShowConnectionPopUp(SerialPortUtilityPro serialPort)
    {
        connectingPopupCanvas.ShowCanvas();
        string connectingText = "Connect with ";
        LocalizationManager.TryGetTranslation("Popup/Connecting", out connectingText);
        connectingText += PlayerPrefs.GetString(serialPort.SerialNumber, serialPort.SerialNumber);
        connectingPopupCanvas.GetComponentInChildren<TMP_Text>()?.SetText(connectingText);
    }

    /// <summary>
    /// Retrieves the value associated with the given name from the CurrentValues dictionary.
    /// If the value is not found, it returns the provided defaultValue.
    /// The method trims the resulting string of any whitespace characters.
    /// </summary>
    public static string GetValueByName(string name, string defaultValue = "")
    {
        string resultString = "";

        if (!CurrentValues.TryGetValue(name.Replace(" ", ""), out resultString))
        {
            Debug.LogError("Key not found: " + name);
        }

        if (string.IsNullOrEmpty(resultString)) resultString = defaultValue;

        return resultString.Replace(" ", "");
    }

    /// <summary>
    /// Retrieves the value associated with the given name from the given port.
    /// If the value is not found, it returns the provided defaultValue.
    /// The method trims the resulting string of any whitespace characters.
    /// </summary>
    public static string GetValueByName(SerialPortUtilityPro port, string name, string defaultValue = "")
    {
        if (port == null) return defaultValue;

        if (!ActivePorts.ContainsKey(port)) return defaultValue;

        string resultString = "";
        
        if (!ActivePorts[port].TryGetValue(name, out resultString))
        {
            Debug.LogError("Key not found: " + name);
        }

        if (string.IsNullOrEmpty(resultString)) resultString = defaultValue;

        return resultString.Replace(" ", "");
    }

    /// <summary>
    /// Retrieves the value associated with the given name from the CurrentValues dictionary and converts it to a float.
    /// If the value is not found, it returns the provided defaultValue.
    /// If the value is not a valid float, it logs a warning and returns the defaultValue.
    /// </summary>
    public static float GetValueByNameAsFloat(string name, float defaultValue = 0f)
    {
        float value = 0f;
        string resultString = "";

        if (!CurrentValues.TryGetValue(name, out resultString))
        {
            Debug.LogError("Key not found: " + name);
        }

        if (string.IsNullOrWhiteSpace(resultString)) return defaultValue;

        try
        {
            value = Convert.ToSingle(resultString, new CultureInfo("en-US"));
        }
        catch (Exception ex)
        {
            Debug.LogWarning(name + ": " + ex.Message);
            return defaultValue;
        }

        return value;
    }

    /// <summary>
    /// Retrieves the value associated with the given name from the given port and converts it to a float.
    /// If the value is not found, it returns the provided defaultValue.
    /// </summary>
    public static float GetValueByNameAsFloat(SerialPortUtilityPro port, string name, float defaultValue = 0f)
    {
        if (port == null) return defaultValue;

        if (!ActivePorts.ContainsKey(port)) return defaultValue;

        float value = 0f;
        string resultString = "";

        if (!ActivePorts[port].TryGetValue(name, out resultString))
        {
            Debug.LogError("Key not found: " + name);
        }

        if (string.IsNullOrEmpty(resultString)) return defaultValue;

        value = Convert.ToSingle(resultString, new CultureInfo("en-US"));

        return value;
    }

    /// <summary>
    /// Retrieves the name associated with the given serial port.
    /// If a name has been saved in PlayerPrefs using the serial port number as the key, it is retrieved and returned.
    /// If no name is found in PlayerPrefs, the serial port number is used as the default name.
    /// </summary>
    /// <param name="port">The serial port to retrieve the name for.</param>
    /// <returns>The name associated with the serial port.</returns>
    public static string GetComPortName(SerialPortUtilityPro port)
    {
        string name = PlayerPrefs.GetString(port.SerialNumber, port.SerialNumber);
        return name;
    }

    /// <summary>
    /// Calculates the total current by summing the currents of fan1, fan2, and the peltier device.
    /// Retrieves the individual currents using their respective keys from the given serial port.
    /// </summary>
    /// <param name="port">The serial port to retrieve the current values from.</param>
    /// <returns>The total current as the sum of fan1, fan2, and peltier currents.</returns>
    public static float GetCurrent(SerialPortUtilityPro port)
    {
        float fan1Current = XPCReader.GetValueByNameAsFloat(port, "L1A");
        float fan2Current = XPCReader.GetValueByNameAsFloat(port, "L2A");
        float peltierCurrent = XPCReader.GetValueByNameAsFloat(port, "PLA");

        float totalCurrent = fan1Current + fan2Current + peltierCurrent;

        return totalCurrent;
    }
}
