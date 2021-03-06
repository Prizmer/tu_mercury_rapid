﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.Threading;

using System.IO;
using System.IO.Ports;
using System.Net.Sockets;

//при переносе в сервер опрос не забыть поменять namespace
namespace teplouchetapp
{
    public delegate int FindPacketSignature(Queue<byte> queue);

    public interface VirtualPort
    {
        int WriteReadData(FindPacketSignature func, byte[] out_buffer, ref byte[] in_buffer, int out_length, int target_in_length, uint pos_count_data_size = 0, uint size_data = 0, uint header_size = 0);
        string GetName();
        bool isOpened();
        SerialPort getSerialPortObject();

        bool OpenPort();
        void ClosePort();
    }

    public sealed class TcpipPort : VirtualPort
    {
        private string m_address = "127.0.0.1";
        private int m_port = 80;
        private ushort m_write_timeout = 100;
        private ushort m_read_timeout = 100;
        private int m_delay_between_sending = 100;

        public string GetName()
        {
            return "tcp/ip " + m_address + ":" + m_port + " ";
        }

        public TcpipPort(string address, int port, ushort write_timeout, ushort read_timeout, int delay_between_sending)
        {
            m_address = address;
            m_port = port;
            m_write_timeout = write_timeout;
            m_read_timeout = read_timeout;
            m_delay_between_sending = delay_between_sending;
        }

        public void Write(byte[] m_cmd, int leng)
        { 
        
        }

        public int Read(ref byte[] data)
        {
            return 0;
        }

        public int WriteReadData(FindPacketSignature func, byte[] out_buffer, ref byte[] in_buffer, int out_length, int target_in_length, uint pos_count_data_size = 0, uint size_data = 0, uint header_size = 0)
        {
            int reading_size = 0;
            using (TcpClient tcp = new TcpClient())
            {
                Queue<byte> reading_queue = new Queue<byte>(8192);

                try
                {
                    Thread.Sleep(m_delay_between_sending);

                    IAsyncResult ar = tcp.BeginConnect(m_address, m_port, null, null);
                    using (WaitHandle wh = ar.AsyncWaitHandle)
                    {
                        if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(10), false))
                        {
                            throw new TimeoutException();
                        }
                        else
                        {
                            if (tcp.Client.Connected)
                            {
                                WriteToLog("connected");

                                tcp.Client.ReceiveTimeout = m_read_timeout;
                                tcp.Client.SendTimeout = m_write_timeout;

                                // посылка данных
                                if (tcp.Client.Send(out_buffer, out_length, SocketFlags.None) == out_length)
                                {
                                    string str = String.Join(" ", out_buffer);
                                    WriteToLog("Tx: " + str);

                                    uint elapsed_time_count = 0;

                                    Thread.Sleep(50);

                                    // чтение данных
                                    while (elapsed_time_count < m_read_timeout)
                                    {
                                        if (tcp.Client.Available > 0)
                                        {
                                            try
                                            {
                                                byte[] tmp_buff = new byte[tcp.Available];
                                                int readed_bytes = tcp.Client.Receive(tmp_buff, 0, tmp_buff.Length, SocketFlags.None);

                                                for (int i = 0; i < readed_bytes; i++)
                                                {
                                                    reading_queue.Enqueue(tmp_buff[i]);
                                                }
                                                str = String.Join(" ", tmp_buff);
                                                WriteToLog("RxCorrect: " + str);
                                                
                                            }
                                            catch (Exception ex)
                                            {
                                                WriteToLog("Receive: " + ex.Message);
                                            }
                                        }
                                        elapsed_time_count += 50;
                                        Thread.Sleep(50);
                                    }

                                    int pos = -1;
                                    if ((pos = func(reading_queue)) >= 0)
                                    {
                                        for (int i = 0; i < pos; i++)
                                        {
                                            reading_queue.Dequeue();
                                        }

                                        byte[] temp_buffer = new byte[reading_size = reading_queue.Count];
                                        temp_buffer = reading_queue.ToArray();

                                        if (target_in_length == 0)
                                        {
                                            if (reading_size > pos_count_data_size)
                                            {
                                                target_in_length = Convert.ToInt32(temp_buffer[pos_count_data_size] * size_data + header_size);
                                            }
                                        }

                                        if (target_in_length > 0)
                                        {
                                            if (reading_size >= target_in_length)
                                            {
                                                reading_size = target_in_length;
                                                for (int i = 0; i < target_in_length && i < in_buffer.Length; i++)
                                                {
                                                    in_buffer[i] = temp_buffer[i];
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                WriteToLog("Is not tcp connected");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteToLog(ex.Message);
                    return -1;
                }
                finally
                {
                    reading_queue.Clear();
                }
            }

            return reading_size;
        }

        public void WriteToLog(string str)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter("TcpPorts.log", true, Encoding.Default))
                {
                    sw.WriteLine(DateTime.Now.ToString() + ": " + GetName() + ": " + str);
                }
            }
            catch
            {
            }
        }

        public bool isOpened()
        {
            return true;
        }


        public SerialPort getSerialPortObject()
        {
            throw new NotImplementedException();
        }


        public bool OpenPort()
        {
            return true;
        }

        public void ClosePort()
        {

        }
    }

    public sealed class ComPort : VirtualPort, IDisposable
    {
        private SerialPort m_Port;
        private string m_name = "COM1";
        private int m_baudrate = 9600;
        private int m_data_bits = 8;
        private Parity m_parity = Parity.None;
        private StopBits m_stop_bits = StopBits.One;
        private ushort m_write_timeout = 500;
        private ushort m_read_timeout = 1000;
        private byte m_attemts = 1;

        public string GetName()
        {
            return m_name + " ";
        }

        public ComPort(byte number, int baudrate, byte data_bits, byte parity, byte stop_bits, ushort write_timeout, ushort read_timeout, byte attemts)
        {
            m_name = "COM" + Convert.ToString(number);
            m_baudrate = baudrate;
            m_data_bits = data_bits;
            m_parity = (Parity)parity; 
            m_stop_bits = (StopBits)stop_bits;
            m_write_timeout = write_timeout;
            m_read_timeout = read_timeout;
            m_attemts = attemts;

            try
            {
                m_Port = new SerialPort(m_name, m_baudrate, m_parity, m_data_bits, m_stop_bits);

                /*ELF: для работы с elf108*/
                m_Port.DtrEnable = true;
                m_Port.RtsEnable = true;
            }
            catch (Exception ex)
            {
                #if (DEBUG)
                    WriteToLog("Create " + m_name + ": " + ex.Message);
                #endif
            }
        }
       
        public ComPort(SerialPort sp, byte attempts, ushort read_timout)
        {

            m_attemts = attempts;
            m_name = sp.PortName;
            m_baudrate = sp.BaudRate;
            m_data_bits = sp.DataBits;
            m_parity = sp.Parity;
            m_stop_bits = sp.StopBits;
            m_read_timeout = read_timout;

            try
            {
                m_Port = new SerialPort(m_name, m_baudrate, m_parity, m_data_bits, m_stop_bits);
                /*ELF: для работы с elf108*/
                m_Port.DtrEnable = true;
                //m_Port.RtsEnable = true;
            }
            catch (Exception ex)
            {

            }
        }
        
        
        public void Dispose()
        {
            if (m_Port != null)
            {
                m_Port.Dispose();
            }
        }

        public bool OpenPort()
        {
            try
            {
                if (m_Port != null)
                {
                    if (!m_Port.IsOpen)
                    {
                        m_Port.Open();
                    }
                }

                return m_Port.IsOpen;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public void ClosePort()
        {
            try
            {
                if (m_Port != null)
                {
                    if (m_Port.IsOpen)
                    {
                        m_Port.Close();
                        //m_Port = null;
                    }
                }
            }
            catch (Exception ex)
            {
#if (DEBUG)
                WriteToLog("Close " + m_name + ": " + ex.Message);
#endif
            }
        }

        /// <summary>
        /// Производит запись команды в COM порт и последующее чтение входных данных с учетом особенностей 
        /// всех драйверов
        /// </summary>
        /// <param name="func">Метод возвращающий int значение начала полезных данных в ответе счетчика</param>
        /// <param name="out_buffer">Данные отправляемые на счетчик</param>
        /// <param name="in_buffer">Полезные данные выделенные из ответа счетчика</param>
        /// <param name="out_length">Длина полезных данных в массиве отправляемых на счетчик данных</param>
        /// <param name="target_in_length">Ожидаемая длина полезных данных ответа счетчика. -1 если все пришедшие данные считаются полезными,
        /// 0 - если для выделения полезных данных требуется использовать необязательные параметры.</param>
        /// <param name="pos_count_data_size">Первый байт полезных данных ответа счетчика</param>
        /// <param name="size_data">Размер полезных данных ответа</param>
        /// <param name="header_size">Размер заголовка</param>
        /// <returns>int количество полезных данных ответа</returns>
        public int WriteReadData(FindPacketSignature func, byte[] out_buffer, ref byte[] in_buffer, int out_length, int target_in_length,
            uint pos_count_data_size = 0, uint size_data = 0, uint header_size = 0)
        {
            int reading_size = 0;
            if (isOpened())
            {
                //очередь для поддержки делегатов в старых драйверах
                Queue<byte> reading_queue = new Queue<byte>(8192);
                //список считываемых байт
                List<byte> readBytesList = new List<byte>(8192);

                #region БЛОК ЗАПИСИ В ПОРТ

                try
                {
                    //пишем в порт команду, ограниченную out_length
                    m_Port.Write(out_buffer, 0, out_length);
                }
                catch (Exception ex)
                {
                    WriteToLog("WriteReadData: Write to port error: " + ex.Message);
                    return 0;
                }

                #endregion

                #region БЛОК ЧТЕНИЯ ИЗ ПОРТА

                Thread.Sleep(100);
                int elapsed_time_count = 100;

                while (elapsed_time_count <= m_read_timeout)
                {
                    if (m_Port.BytesToRead > 0)
                    {
                        try
                        {
                            byte[] tmp_buff = new byte[m_Port.BytesToRead];
                            int read_bytes = m_Port.Read(tmp_buff, 0, tmp_buff.Length);

                            readBytesList.AddRange(tmp_buff);
                        }
                        catch (Exception ex)
                        {
                            WriteToLog("WriteReadData: Read from port error: " + ex.Message);
                        }
                    }

                    elapsed_time_count += 100;
                    Thread.Sleep(100);
                }

                #endregion

                #region БЛОК РАЗБОРА ДАННЫХ

                /*TODO: Откуда взялась константа 4, почему 4?*/
                if (readBytesList.Count >= 4)
                {
                    /*попытаемся определить начало полезных данных в буфере-на-вход
                        при помощи связанного делегата*/
                    for (int i = 0; i < readBytesList.Count; i++)
                        reading_queue.Enqueue(readBytesList[i]);

                    int pos = func(reading_queue);
                    if (pos >= 0)
                    {
                        //избавимся от лишних данных спереди
                        for (int i = 0; i < pos; i++)
                        {
                            reading_queue.Dequeue();
                        }

                        //оставшиеся данные преобразуем обратно в массив
                        reading_size = reading_queue.Count;
                        byte[] temp_buffer = reading_queue.ToArray();
                        //WriteToLog(BitConverter.ToString(temp_buffer));


                        if (target_in_length == 0)
                        {
                            //если длина полезных данных ответа определена как 0, произведем расчет по необязательнм параметрам
                            if (reading_size > pos_count_data_size)
                                target_in_length = Convert.ToInt32(temp_buffer[pos_count_data_size] * size_data + header_size);
                        } 
                        else if (target_in_length == -1) 
                        {
                            //если как -1, считаем, что все данные пришедшие в буфер полезные
                            in_buffer = new byte[reading_size];

                            for (int i = 0; i < in_buffer.Length; i++)
                                in_buffer[i] = temp_buffer[i];

                            return reading_size;
                        }
                        else if (target_in_length > 0 && reading_size >= target_in_length)
                        {
                            reading_size = target_in_length;
                            for (int i = 0; i < target_in_length && i < in_buffer.Length; i++)
                            {
                                in_buffer[i] = temp_buffer[i];
                            }
                            return reading_size;
                        }

                    }
                }

                #endregion
            }
            else
            {
                WriteToLog("Open port Error");
            }

            return reading_size;
        }

        /// <summary>
        /// Test version for teplouchet meters
        /// </summary>
        /// <param name="out_buffer"></param>
        /// <param name="in_buffer"></param>
        /// <param name="target_in_length"></param>
        /// <returns></returns>
        public int WriteReadData(byte[] out_buffer, ref byte[] in_buffer, int target_in_length = -1)
        {
            int reading_size = 0;
            if (isOpened()) ClosePort();
            if (OpenPort())
            {
                //очередь для поддержки делегатов в старых драйверах
                Queue<byte> reading_queue = new Queue<byte>(8192);
                //список считываемых байт
                List<byte> readBytesList = new List<byte>(8192);

                #region БЛОК ЗАПИСИ В ПОРТ

                try
                {
                    //пишем в порт команду, ограниченную out_length
                    m_Port.Write(out_buffer, 0, out_buffer.Length);
                }
                catch (Exception ex)
                {
                    WriteToLog("WriteReadData: Write to port error: " + ex.Message);
                    return 0;
                }

                #endregion

                #region БЛОК ЧТЕНИЯ ИЗ ПОРТА

                Thread.Sleep(100);
                int elapsed_time_count = 100;

                while (elapsed_time_count <= m_read_timeout)
                {
                    if (m_Port.BytesToRead > 0)
                    {
                        try
                        {
                            byte[] tmp_buff = new byte[m_Port.BytesToRead];
                            m_Port.Read(tmp_buff, 0, tmp_buff.Length);

                            readBytesList.AddRange(tmp_buff);
                        }
                        catch (Exception ex)
                        {
                            WriteToLog("WriteReadData: Read from port error: " + ex.Message);
                        }
                    }

                    elapsed_time_count += 100;
                    Thread.Sleep(100);
                }

                #endregion

                #region БЛОК РАЗБОРА ДАННЫХ

                if (readBytesList.Count >= 1)
                {

                    //TODO: узнать как переводится очередь в массив и избавиться от нее
                    for (int i = 0; i < readBytesList.Count; i++)
                        reading_queue.Enqueue(readBytesList[i]);
                    reading_size = reading_queue.Count;
                    byte[] temp_buffer = reading_queue.ToArray();


                    if (target_in_length == -1)
                        {
                            //если как -1, считаем, что все данные пришедшие в буфер полезные
                            in_buffer = new byte[reading_size];

                            for (int i = 0; i < in_buffer.Length; i++)
                                in_buffer[i] = temp_buffer[i];

                            return reading_size;
                        }
                        else if (target_in_length > 0 && reading_size >= target_in_length)
                        {
                            reading_size = target_in_length;
                            for (int i = 0; i < target_in_length && i < in_buffer.Length; i++)
                            {
                                in_buffer[i] = temp_buffer[i];
                            }
                            return reading_size;
                        }
                }
                #endregion

                ClosePort();
            }
            else
            {
                WriteToLog("Open port Error");
                return 0;
            }

            return reading_size;
        }


        public void WriteToLog(string str)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter("ComPorts.log", true, Encoding.Default))
                {
                    sw.WriteLine(DateTime.Now.ToString() + ": " + GetName() + ": " + str);
                }
            }
            catch
            {
            }
        }


        public bool isOpened()
        {
            return m_Port.IsOpen;
        }

        public SerialPort getSerialPortObject()
        {
            return this.m_Port;
        }
    }
}