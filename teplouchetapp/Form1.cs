using System;
using System.Collections.Generic;
using System.Data;
using System.Windows.Forms;

using System.IO.Ports;
using ExcelLibrary.SpreadSheet;
using System.Configuration;
using System.Threading;


using System.Collections.Specialized;
//using System.Configuration.Assemblies;
using Drivers.LibMeter;
using Drivers.Mercury23XDriver;
using PollingLibraries.LibPorts;



namespace teplouchetapp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        Mercury23XDriver Meter = null;
        VirtualPort Vp = null;

        volatile bool bStopProcess = false;
        bool bPollOnlyOffline = false;

        //default settings for input *.xls file
        int flatNumberColumnIndex = 0;
        int factoryNumberColumnIndex = 1;
        int firstRowIndex = 1;

        private bool initMeterDriver(uint mAddr, string mPass, VirtualPort virtPort)
        {
            if (virtPort == null) return false;

            try
            {
                Meter = new Mercury23XDriver();
                Meter.Init(mAddr, mPass, virtPort);
                return true;
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка инициализации драйвера: " + ex.Message);
                return false;
            }
        }

        private bool refreshSerialPortComboBox()
        {
            try
            {
                string[] portNamesArr = SerialPort.GetPortNames();
                comboBox1.Items.AddRange(portNamesArr);
                if (comboBox1.Items.Count > 0)
                {
                    int startIndex = 0;
                    comboBox1.SelectedIndex = startIndex;
                    return true;
                }
                else
                {
                    WriteToStatus("В системе не найдены доступные COM порты");
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка при обновлении списка доступных COM портов: " + ex.Message);
                return false;
            }
        }


        string exportFilename = "result";
        private bool setVirtualSerialPort(bool setTcp)
        {
            ushort read_timeout = (ushort)numericUpDown1.Value;
            byte attempts = 1;
            uint mAddr = 0x01;
            string mPass = "111111";

            if (!setTcp){
                try
                {
                    SerialPort m_Port = new SerialPort(comboBox1.Items[comboBox1.SelectedIndex].ToString());

                    m_Port.BaudRate = int.Parse(ConfigurationSettings.AppSettings["baudrate"]);
                    m_Port.DataBits = int.Parse(ConfigurationSettings.AppSettings["databits"]);
                    m_Port.Parity = (Parity)int.Parse(ConfigurationSettings.AppSettings["parity"]);
                    m_Port.StopBits = (StopBits)int.Parse(ConfigurationSettings.AppSettings["stopbits"]);
                    m_Port.DtrEnable = bool.Parse(ConfigurationSettings.AppSettings["dtr"]);



                    //meters initialized by secondary id (factory n) respond to 0xFD primary addr
                    Vp = new ComPort(m_Port, attempts, read_timeout, 400);
                    if (!initMeterDriver(mAddr, mPass, Vp)) return false;

                    //check vp settings
                    SerialPort tmpSP = (SerialPort)Vp.GetPortObject();
                    toolStripStatusLabel2.Text = String.Format("{0} {1}{2}{3} DTR: {4} RTimeout: {5} ms", tmpSP.PortName, tmpSP.BaudRate, tmpSP.Parity, tmpSP.StopBits, tmpSP.DtrEnable, read_timeout);

                    exportFilename = String.Format("{0}_{1}-{2}", tmpSP.PortName, numericUpDown2.Value, numericUpDown3.Value);

                    return true;
                }
                catch (Exception ex)
                {
                    WriteToStatus("Ошибка создания виртуального COM порта: " + ex.Message);
                    return false;
                }
            }else{
                try
                {
                    ushort write_timeout = 100;
                    int delay_between_sendings = 50;

                    //TODO: сделать это подсосом из xml
                    NameValueCollection loadedAppSettings = new NameValueCollection();
                    loadedAppSettings.Add("localEndPointIp", this.listBox1.SelectedItem.ToString());


                    Vp = new TcpipPort(textBox1.Text, (int)numericUpDown4.Value, write_timeout, read_timeout, delay_between_sendings, loadedAppSettings);
                    if (!initMeterDriver(mAddr, mPass, Vp)) return false;
                    
                    toolStripStatusLabel2.Text = String.Format("TCP {0}:{1} RTimeout: {2} ms", textBox1.Text, (int)numericUpDown4.Value, read_timeout);


                    exportFilename = String.Format("{0}_{1}-{2}", textBox1.Text + "_" + numericUpDown4.Value, numericUpDown2.Value, numericUpDown3.Value);

                    return true;
                }
                catch (Exception ex)
                {
                    WriteToStatus("Ошибка создания виртуального TCP порта: " + ex.Message);
                    return false;
                }
               
            }
        }

        private void WriteToStatus(string str)
        {
            MessageBox.Show(str, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (!refreshSerialPortComboBox()) return;
            if (!setVirtualSerialPort(false))  return;


            numericUpDown3.Minimum = numericUpDown2.Value;
            fillMainTable();

            comboBox1.SelectedIndexChanged += new EventHandler(comboBox1_SelectedIndexChanged);
            numericUpDown1.ValueChanged +=new EventHandler(numericUpDown1_ValueChanged);
            meterPinged += new EventHandler(Form1_meterPinged);
            pollingEnd += new EventHandler(Form1_pollingEnd);
            portProblems += new EventHandler(Form1_portProblems);

            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    this.listBox1.Items.Add(ip.ToString());
                }
            }

            this.listBox1.SelectedIndex = 0;
        }


        DataTable dt = new DataTable("meters");
        public string worksheetName = "Лист1";

        string[] captions = { "A+", "A-", "R+", "R-" };
        string[] colNames = { "colAp", "colAm", "colRp", "colRm" };
        private void createMainTable(ref DataTable dt)
        {
            //creating columns for internal data table
            DataColumn column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Сетевой №";
            column.ColumnName = "colFlat";

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Серийный №";
            column.ColumnName = "colFactory";


            if (cbWithData.Checked)
            {
                for (int j = 0; j < 5; j++)
                {
                    for (int i = 0; i < captions.Length; i++)
                    {
                        column = dt.Columns.Add();
                        column.DataType = typeof(string);
                        column.Caption = "T" + j + " " + captions[i];
                        column.ColumnName = colNames[i] + j;
                    }

                    if (j == 0 && rbT0.Checked) break;
                }
            }

            DataRow captionRow = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++)
                captionRow[i] = dt.Columns[i].Caption;
            dt.Rows.Add(captionRow);
        }

        private void fillMainTable()
        {

            dt = new DataTable();
            createMainTable(ref dt);

            //filling internal data table with *.xls file data according to *.config file
            int cnt = 0;
            for (int i = (int)numericUpDown2.Value; i <= (int)numericUpDown3.Value; i++)
            {
                DataRow dataRow = dt.NewRow();
                dataRow[0] = i;
                dt.Rows.Add(dataRow);
                cnt++;
            }

            toolStripProgressBar1.Maximum = cnt;

            dgv1.DataSource = dt;
        }


        public event EventHandler meterPinged;
        private void incrProgressBar()
        {
            if (toolStripProgressBar1.Value < toolStripProgressBar1.Maximum)
            {
                toolStripProgressBar1.Value += 1;
                toolStripStatusLabel1.Text = String.Format("({0}/{1})", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
            }
        }

        void Form1_meterPinged(object sender, EventArgs e)
        {
            incrProgressBar();
        }

        public event EventHandler pollingEnd;
        void Form1_pollingEnd(object sender, EventArgs e)
        {
            comboBox1.Enabled = true;
            button2.Enabled = true;
            button5.Enabled = true;
            button6.Enabled = false;
            numericUpDown1.Enabled = true;
            checkBox1.Enabled = true;
            button1.Enabled = true;

            textBox1.Enabled = true;
            numericUpDown4.Enabled = true;

            numericUpDown2.Enabled = true;
            numericUpDown3.Enabled = true;
        }

        public event EventHandler portProblems;
        void Form1_portProblems(object sender, EventArgs e)
        {
            WriteToStatus("Can't open port");
        }



        private void button2_Click(object sender, EventArgs e)
        {
            if (!setVirtualSerialPort(false)) return;


            toolStripProgressBar1.Value = 0;

            disableContols();

            Thread pingThr = new Thread(pingMeters);
            pingThr.Start((object)dt);
        }

        private void pingMeters(Object metersDt)
        {
            DataTable dt = (DataTable)metersDt;
            int columnIndexFactory = 1;

            List<string> factoryNumbers = new List<string>();
            if (Vp.OpenPort())
            {
                for (int i = 1; i < dt.Rows.Count; i++)
                {
                    object oColLocalAddr = dt.Rows[i][0];
                    object oColFactory = dt.Rows[i][columnIndexFactory];
                    //object oColResult = dt.Rows[i][columnIndexResult];

                    //check if already polled
                    if (bPollOnlyOffline && oColFactory != null && oColFactory.ToString() != "Нет связи")
                        continue;

                    Thread.Sleep(5);
                    Meter.ChangeMAddress(uint.Parse(oColLocalAddr.ToString()));
                    string tmpSerialNumber = "";

                    if (Meter.OpenLinkCanal() && Meter.ReadSerialNumber(ref tmpSerialNumber))
                    {
                        dt.Rows[i][columnIndexFactory] = tmpSerialNumber;

                        if (cbWithData.Checked)
                        {
                            byte[] mAnswBytes = new byte[1];

                            // перебираем тарифы
                            for (byte j = 0; j < 5; j++)
                            {
                                // получим данные ток по T0
                                if (Meter.ReadCurrentMeterageToTarif(j, ref mAnswBytes))
                                {
                                    for (ushort k = 0; k < captions.Length; k++)
                                    {
                                        float val = -1f;
                                        Meter.GetValueFromMeterageToTarifAnswer(k, mAnswBytes, ref val);                                  
                                        dt.Rows[i][colNames[k] + j] = val;
                                    }
                                }
                                else
                                {
                                    dt.Rows[i][j * 4] = "Ошибка";
                                }

    
                                if (j == 0 && rbT0.Checked) break;
                            }

                        }
                    }
                    else
                    {
                        dt.Rows[i][columnIndexFactory] = "Нет связи";
                    }

                    Invoke(meterPinged);

                    if (bStopProcess)
                    {
                        bStopProcess = false;
                        break;
                    }
                }

                Vp.Close();
            }
            else
            {
                Invoke(portProblems);
            }

            Invoke(pollingEnd);
        }


        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort(false);
        }


        private void button5_Click(object sender, EventArgs e)
        {
            sfd1.FileName = exportFilename + ".xls";
            sfd1.Filter = "Xls|*.xls";
            if (sfd1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                //create new xls file
                string file = sfd1.FileName;
                Workbook workbook = new Workbook();
                Worksheet worksheet = new Worksheet(worksheetName);

                //office 2010 will not open file if there is less than 100 cells
                for (int i = 0; i < 100; i++)
                    worksheet.Cells[i, 0] = new Cell("");

                //copying data from data table
                for (int rowIndex = 0; rowIndex < dt.Rows.Count; rowIndex++)
                {
                    for (int colIndex = 0; colIndex < dt.Columns.Count; colIndex++)
                    {
                        worksheet.Cells[rowIndex, colIndex] = new Cell(dt.Rows[rowIndex][colIndex].ToString());
                    }
                }

                workbook.Worksheets.Add(worksheet);
                workbook.Save(file);
            }
        }



        private void button1_Click(object sender, EventArgs e)
        {

        }

        private void button6_Click(object sender, EventArgs e)
        {
            bStopProcess = true;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort(false);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            bPollOnlyOffline = checkBox1.Checked;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                Vp.Close();
            } catch (Exception ex)
            {

            }
        }


        private void numericAddress_ValueChanged(object sender, EventArgs e)
        {
            NumericUpDown numeric = (NumericUpDown)sender;
            numericUpDown3.Minimum = numericUpDown2.Value;
            fillMainTable();
        }

        private void disableContols()
        {
            toolStripProgressBar1.Value = 0;
            toolStripStatusLabel1.Text = "";

            comboBox1.Enabled = false;
            button2.Enabled = false;
            button5.Enabled = false;
            button6.Enabled = true;
            button1.Enabled = false;
            numericUpDown1.Enabled = false;
            checkBox1.Enabled = false;

            textBox1.Enabled = false;
            numericUpDown4.Enabled = false;

            numericUpDown2.Enabled = false;
            numericUpDown3.Enabled = false;
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            if (!setVirtualSerialPort(true)) return;

            disableContols();

            Thread pingThr = new Thread(pingMeters);
            pingThr.Start((object)dt);
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void dgv1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void cbWithData_CheckedChanged(object sender, EventArgs e)
        {
            this.fillMainTable();
        }

        private void rbT0_CheckedChanged(object sender, EventArgs e)
        {
            this.fillMainTable();
        }
    }
}
