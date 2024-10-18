using NPOI.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class TPCFirmwareUpdater : MonoBehaviour
{
    public static TPCFirmwareUpdater Instance { get; private set; }

    [SerializeField] private ProgressPopUp progressPopUp;

    public bool UpdateInProgress { get; private set; }

    private XPCReader reader;
    private bool waitForFlashMode;
    private string firmwareFilePath;
    private bool sendNextUpdate = true;
    private bool updating;
    private int totalUpdateIterations;
    private int currentUpdateIterations;
    private int byteIndex;
    private byte[] updateData;
    private byte[] buffer;
    private long fileSize;
    private bool endUpdateOnNextInput;
    int checkCount;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        reader = XPCReader.Instance;
        XPCReader.OnJSONReceived += XPCReader_OnJSONReceived;
    }

    /// <summary>
    /// Called when the XPCReader receives a JSON update.
    /// It checks if the update is a firmware update and if so, it checks if the update is finished.
    /// If the update is finished, it calls EndUpdate to hide the progress pop up.
    /// If the update is not finished, it increments a counter and if the counter is equal to 3, it calls EndUpdate.
    /// </summary>
    /// <param name="arg1">The serial port that sent the update.</param>
    /// <param name="arg2">The dictionary of values in the JSON update.</param>
    private void XPCReader_OnJSONReceived(SerialPortUtility.SerialPortUtilityPro arg1, Dictionary<string, string> arg2)
    {
        if (reader.CurrentSerialPort && reader.CurrentSerialPort == arg1)
        {
            if (endUpdateOnNextInput)
            {
                string firmwareString = XPCReader.GetValueByName("UCSV", "unknown");

                if (firmwareString != "0.00" && firmwareString != "unknown")
                {
                    EndUpdate();
                }
                else
                {
                    checkCount++;

                    if(checkCount >= 3)
                    {
                        EndUpdate();
                        checkCount = 0;
                    }
                }
            }
        }
    }

    public void StartFirmwareUpdate(string firmwarePath)
    {
        UpdateInProgress = true;
        firmwareFilePath = firmwarePath;
        StartCoroutine(HandleFirmwareUpdate());
    }

    /// <summary>
    /// Handles the firmware update. This coroutine is called when the StartFirmwareUpdate method is called.
    /// It first checks if the controller is in flash mode. If it is not, it sends the command to enable flash mode.
    /// Then it reads the firmware file and calculates the CRC32 value of the file.
    /// Then it sends the CRC32 value, the file size and the page size to the controller.
    /// After that, it sends the firmware data in chunks of 4096 bytes to the controller.
    /// During the update, it shows the progress in the progress pop up.
    /// When the update is finished, it calls EndUpdate to hide the progress pop up.
    /// </summary>
    private IEnumerator HandleFirmwareUpdate()
    {
        currentUpdateIterations = 0;
        byteIndex = 0;
        progressPopUp.SetHeader("Updating Firmware");
        progressPopUp.ShowPopUp();

        reader.CurrentSerialPort.ReadCompleteEventObject.AddListener(DataReceivedHandler);

        waitForFlashMode = !XPCReader.CurrentControllerInFlashMode;

        if (waitForFlashMode) reader.AddUpdateString("FLSH", "1.00");

        while (waitForFlashMode)
        {
            yield return null;
        }

        reader.SendUpdates = false;

        if (string.IsNullOrWhiteSpace(firmwareFilePath) || !File.Exists(firmwareFilePath))
        {
            Debug.LogError("Firmware not found");
            yield return null;
        }

        updating = true;

        fileSize = new FileInfo(firmwareFilePath).Length;
        updateData = File.ReadAllBytes(firmwareFilePath);

        CRC32 crc = new CRC32();
        ulong byteCRC = crc.ByteCRC(ref updateData);

        uint[] data = new uint[3];
        data[0] = (uint)byteCRC;
        data[1] = (uint)fileSize;
        data[2] = (uint)GetPageSize(fileSize);

        byte[] bytes = data.SelectMany(BitConverter.GetBytes).ToArray();

        sendNextUpdate = false;
        reader.CurrentSerialPort.Write(bytes);

        totalUpdateIterations = updateData.Length / 4096 + 1;

        while (updating)
        {
            if (sendNextUpdate)
            {
                buffer = GetBuffer();
                sendNextUpdate = false;
                currentUpdateIterations++;
                reader.CurrentSerialPort.Write(buffer);
            }
            yield return null;
        }
    }

    /// <summary>
    /// Retrieves a buffer of up to 4096 bytes from the firmware update data starting at the current byte index.
    /// If the remaining data is less than 4096 bytes, only the available bytes are returned.
    /// The method updates the byte index to reflect the bytes that have been read.
    /// </summary>
    private byte[] GetBuffer()
    {
        byte[] tmp = new byte[4096];
        if ((fileSize - 1 - byteIndex) < 4096)
        {
            tmp = new byte[fileSize - byteIndex];
            int i = 0;
            while (byteIndex < fileSize)
            {
                tmp[i] = updateData[byteIndex];
                i++;
                byteIndex++;
            }
        }
        else
        {
            int i = 0;

            while (i < 4096)
            {
                tmp[i] = updateData[byteIndex];
                i++;
                byteIndex++;
            }
        }
        return tmp;
    }

    private float GetPageSize(long fileSize)
    {
        return Mathf.Ceil(fileSize / 256);
    }

    /// <summary>
    /// Handles the data received event from the serial port.
    /// It checks if the controller is in flash mode and if the update is finished.
    /// It updates the progress in the progress pop up.
    /// </summary>
    /// <param name="arg0">The object received from the serial port.</param>
    private void DataReceivedHandler(object arg0)
    {
        if (waitForFlashMode)
        {
            if (arg0.ToString() == "{\"flash\":\"rdy\"}")
            {
                waitForFlashMode = false;
            }
            return;
        }

        if (arg0.ToString() == "{\"flash\":\"done\"}")
        {
            reader.CurrentSerialPort.ReadCompleteEventObject.RemoveListener(DataReceivedHandler);
            reader.SendUpdates = true;
            updating = false;
            endUpdateOnNextInput = true;

            UpdateInProgress = false;
        }
        else
        {
            if (currentUpdateIterations >= totalUpdateIterations)
            {
                reader.CurrentSerialPort.ReadCompleteEventObject.RemoveListener(DataReceivedHandler);
                reader.SendUpdates = true;
                updating = false;
                endUpdateOnNextInput = true;
                UpdateInProgress = false;
                return;
            }

            sendNextUpdate = true;
        }

        progressPopUp.SetProgress((float)currentUpdateIterations / (float)totalUpdateIterations);
    }

    private void EndUpdate()
    {
        progressPopUp.HidePopUp();
        endUpdateOnNextInput = false;
    }
}
