using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SerialPortUtility;
using System.Globalization;
using I2.Loc;

/// <summary>
/// Converts a json value by the given name to a string, float, int, or bool and calls the corresponding event.
/// </summary>
public class SerialConverter : MonoBehaviour
{
    [Serializable]
    public class SerialValue
    {
        public string ValueName;
        [EnumToggleButtons] public EventType eventType;
        [ShowIf(nameof(eventType), EventType.String)] public string Prefix;
        [ShowIf(nameof(eventType), EventType.String)] public string Suffix;
        [ShowIf(nameof(eventType), EventType.String)] public string StringFormat;
        [ShowIf(nameof(eventType), EventType.String)] public UnityEvent<string> OnMessageRecievedAsString;
        [ShowIf(nameof(eventType), EventType.Float)] public UnityEvent<float> OnMessageRecievedAsFloat;
        [ShowIf(nameof(eventType), EventType.Int)] public UnityEvent<int> OnMessageRecievedAsInt;
        [ShowIf(nameof(eventType), EventType.Bool)] public UnityEvent<bool> OnMessageRecievedAsBool;
    }

    public enum EventType
    {
        String,
        Float,
        Int,
        Bool
    }

    public SerialValue serialValue;
    private XPCReader reader;
    private string valueToSend;
    public string CurrentValue { get; private set; }

    private bool canReceive = true;
    public bool CanReceive
    {
        get { return canReceive; }
        set
        {
            canReceive = value;
        }
    }

    private bool canSend = true;
    public bool CanSend
    {
        get { return canSend; }
        set
        {
            canSend = value;

            if (!canSend) valueToSend = null;
        }
    }

    void Start()
    {
        reader = XPCReader.Instance;
        XPCReader.OnJSONReceived += XPCReader_OnJSONReceived;
    }

    /// <summary>
    /// Handles the JSON message received event.
    /// Validates if the message can be received, checks if the sender's current serial port matches the sender, and ensures the value name is not empty.
    /// If the value name is found in the received message, processes the value based on the event type and invokes the corresponding event.
    /// </summary>
    private void XPCReader_OnJSONReceived(SerialPortUtilityPro sender, Dictionary<string, string> obj)
    {
        if (!CanReceive || reader.CurrentSerialPort != sender || string.IsNullOrWhiteSpace(serialValue.ValueName)) return;

        if (obj.ContainsKey(serialValue.ValueName))
        {
            string valueString = obj[serialValue.ValueName];

            if (!string.IsNullOrWhiteSpace(valueToSend) && valueToSend != valueString) return;

            valueToSend = null;
            CurrentValue = valueString;

            switch (serialValue.eventType)
            {
                case EventType.String:
                    if (!string.IsNullOrWhiteSpace(serialValue.StringFormat))
                    {
                        try
                        {
                            if (LocalizationManager.CurrentLanguage.ToLower() == "german")
                            {
                                double decimalValue = Convert.ToDouble(valueString.Replace(".", ","));
                                valueString = decimalValue.ToString(serialValue.StringFormat);
                            }
                            else
                            {
                                double decimalValue = Convert.ToDouble(valueString);
                                valueString = decimalValue.ToString(serialValue.StringFormat, new CultureInfo("en-US"));
                            }
                        }
                        catch (Exception ex)
                        {
                            string message = ex.Message;
                        }
                    }
                    serialValue.OnMessageRecievedAsString?.Invoke(serialValue.Prefix + valueString + serialValue.Suffix);
                    break;
                case EventType.Float:
                    float floatValue = 0f;
                    try
                    {
                        floatValue = Convert.ToSingle(valueString, new CultureInfo("en-US"));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(serialValue.ValueName + ": " + ex.Message);
                        Debug.LogWarning(serialValue.ValueName + ": " + valueString);
                        floatValue = 0f;
                    }
                    serialValue.OnMessageRecievedAsFloat?.Invoke(floatValue);
                    break;
                case EventType.Int:
                    int intValue = 0;
                    try
                    {
                        intValue = Convert.ToInt32(valueString.Split('.')[0], new CultureInfo("en-US"));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(serialValue.ValueName + ": " + ex.Message);
                        intValue = 0;
                    }
                    serialValue.OnMessageRecievedAsInt?.Invoke(intValue);
                    break;
                case EventType.Bool:
                    bool boolValue = false;
                    boolValue = valueString.Equals("1") || valueString.Equals("1.00") || valueString.Equals("true") || valueString.Equals("True") || valueString.Equals("On") || valueString.Equals("ON");
                    serialValue.OnMessageRecievedAsBool?.Invoke(boolValue);
                    break;
            }
        }
    }

    public void SendUpdate(bool value)
    {
        string valueString = value ? "1.00" : "0.00";
        SendUpdate(valueString);
    }

    public void SendUpdate(float value)
    {
        SendUpdate(value.ToString("0.00").Replace(',', '.'));
    }

    public void SendUpdate(int value)
    {
        SendUpdate(value.ToString() + ".00");
    }

    /// <summary>
    /// Sends an update to the serial port.
    /// Validates if the message can be sent and if the reader is available.
    /// If the conditions are met, it sets the value to send and updates the current value.
    /// Adds the update string to the reader's update dictionary with the specified value name.
    /// </summary>
    public void SendUpdate(string value)
    {
        if (!CanSend || !reader || string.IsNullOrWhiteSpace(value)) return;

        if (reader.CurrentSerialPort) valueToSend = value;

        CurrentValue = value;

        reader.AddUpdateString(serialValue.ValueName, value);
    }
}
