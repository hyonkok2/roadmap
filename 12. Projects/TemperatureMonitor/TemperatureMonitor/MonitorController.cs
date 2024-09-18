﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using TemperatureMonitor.Modbus;
using TemperatureMonitor.Device;
using TemperatureMonitor.Communications;

namespace TemperatureMonitor
{
    public class MonitorController
    {
        #region Event 정의
        public event Action<TemperatureDevice>? OnTemperatureDeviceValueChanged;
        #endregion

        #region Member 변수 정의
        private readonly SerialCommunication? _serialCommunication;
        private readonly TemperatureDevice? _device;

        private readonly Queue<RequestDataSet> _requestQueue = new();
        private readonly List<RequestDataSet> _requestList = new();
        private readonly List<byte> _responseMessage = new();
        private RequestDataSet? _currentRequestDataSet;
        private readonly ManualResetEvent _resetEvent = new(false);
        private readonly Thread _receiveDataHandleThread;
        #endregion

        #region 생성자
        public MonitorController(string portName, int baudRate)
        {
            _serialCommunication = new SerialCommunication(portName, baudRate, dataBit: 8, Parity.None, StopBits.One);// 본 설비에서는 databit 등의 옵션은 고정이므로 여기에 하드코드 함.
            _serialCommunication.OnDataReceived += SerialCommunication_DataReceived;
            _device = new TemperatureDevice(1);// slave 1 번 기준으로 작성함. 필요 시 확장성 고려하여 처리할 것
            _device.OnValueChanged += Device_OnValueChanged;

            InitialModbusDataSet();

            _receiveDataHandleThread = new Thread(ReceivedDataHandle)
            {
                IsBackground = true
            };

            _receiveDataHandleThread.Start();
        }

        #endregion

        #region Public Method

        public bool Start()
        {
            if (_serialCommunication!.OpenPort() == false) return false;

            try
            {
                RequestToDevice();
            }
            catch (Exception) // 로깅은 나중에..
            {
                _serialCommunication!.ClosePort();
                return false;
            }

            return true;
        }

        public void Stop()
        {
            try
            {
                _serialCommunication!.ClosePort();
            }
            catch (Exception)
            {

                throw;
            }
        }
        #endregion

        #region Private Method

        #region 이벤트 핸들러
        private void Device_OnValueChanged()
        {
            OnTemperatureDeviceValueChanged?.Invoke(_device!);
        }

        private void SerialCommunication_DataReceived(byte[] receivedData)
        {
            _responseMessage.AddRange(receivedData);
            _resetEvent.Set();
        }
        #endregion

        #region 정보 초기화 처리
        private void InitialModbusDataSet()
        {
            _requestList.Clear();

            _requestList.Add(new RequestDataSet(new List<ModbusData>
            {
                _device!.ModbusDataDictionary[DeviceDataType.Temperature1],
                _device!.ModbusDataDictionary[DeviceDataType.Temperature2],
            }, 
            _device.SlaveId));

            _requestList.Add(new RequestDataSet(new List<ModbusData>
            {
                _device!.ModbusDataDictionary[DeviceDataType.Alarm1],
                _device!.ModbusDataDictionary[DeviceDataType.Alarm2],
            },
            _device.SlaveId));

            _requestList.Add(new RequestDataSet(new List<ModbusData>
            {
                _device!.ModbusDataDictionary[DeviceDataType.Leak1],
                _device!.ModbusDataDictionary[DeviceDataType.Leak2],
            },
            _device.SlaveId));
        }
        #endregion

        #region 정보 읽기 요청

        private void RequestToDevice()
        {
            if (_serialCommunication!.IsOpened() == false) return;

            if (_requestQueue.Count == 0)
            {
                _requestList.ForEach(x => _requestQueue.Enqueue(x));
            }

            _currentRequestDataSet = _requestQueue.Dequeue();

            _responseMessage.Clear();

            _serialCommunication!.SendMessage(_currentRequestDataSet.RequestMessage.Message);

            // 요청 후 응답이 오지 않는 경우에 대한 처리.. Timeout은 필요하면 추가할 것.. 초기엔 구현하지 않음.
        }
        #endregion

        #region 접수 받은 정보 처리
        private void ReceivedDataHandle(object? obj)
        {
            while (true)
            {
                _resetEvent.WaitOne(1000);
                _resetEvent.Reset();

                if (_responseMessage.Count < 3) continue; // 사이즈가 3보다 커야 데이터 크기 정보를 알 수 있음.

                int expectedDataSize = _responseMessage[2] + 5;  // 예상되는 완성된 사이즈는 슬레이브번호 + 펑션코드 + 데이터 사이즈 정보 + CRC(2byte) 그리고 실제 Data 정보

                if (expectedDataSize > _responseMessage.Count) continue; // 아직 완전히 접수되지 않아서 기다린다.

                if (expectedDataSize < _responseMessage.Count) // 받은 사이즈가 예상보다 크면 비정상으로 처리하지 않음. 다음 요청 수행.
                {
                    RequestToDevice();
                    continue;
                }

                byte[] receivedBytes = _responseMessage.ToArray();

                if(_currentRequestDataSet!.ReceivedBytes.SequenceEqual(receivedBytes)) // 기존 정보와 바뀐 것이 없다면, 다음 요청 수행.
                {
                    RequestToDevice();
                    continue;
                }

                ReceiveMessage receiveMessage = new(receivedBytes); // 받은 정보 처리

                if (receiveMessage.IsValid == false) // 비정상이면 처리하지 않고, 다음 요청 수행.
                {
                    RequestToDevice();
                    continue;
                }

                if (receiveMessage.DataCount != _currentRequestDataSet!.DataSize) // 요청한 것과 받은 정보의 크기가 일치해야 함. //사실 Slave, Function code도 비교해야 하나.. 그냥 넘어감. 추후 필요하면 할 것..
                {
                    RequestToDevice();
                    continue;
                }

                _currentRequestDataSet.ReceivedBytes = receivedBytes;// 현재 받은 정보를 백업함.

                for (int i = 0; i < _currentRequestDataSet!.DataList.Count; i++)
                {
                    _currentRequestDataSet.DataList[i].Value = receiveMessage.DataArray[i];
                }

                RequestToDevice();
            }
            #endregion

        #endregion

        }
    }
}
