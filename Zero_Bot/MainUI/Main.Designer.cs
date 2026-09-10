namespace Zero
{
    partial class Main
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.PID = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.账号 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.名字 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.服务器 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.等级 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.职业 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.血量 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.蓝量 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.金币 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.脚本 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.状态 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.开始 = new System.Windows.Forms.DataGridViewButtonColumn();
            this.停止 = new System.Windows.Forms.DataGridViewButtonColumn();
            this.编辑 = new System.Windows.Forms.DataGridViewButtonColumn();
            this.删除 = new System.Windows.Forms.DataGridViewButtonColumn();
            this.btn_Add = new System.Windows.Forms.Button();
            this.UpdateTimer = new System.Windows.Forms.Timer(this.components);
            this.adjust_the_window = new System.Windows.Forms.Button();
            this.Start_all_processes = new System.Windows.Forms.Button();
            this.Stop_all_processes = new System.Windows.Forms.Button();
            this.Server_Announcement = new System.Windows.Forms.Label();
            this.Clear_list = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.Client_path = new System.Windows.Forms.TextBox();
            this.Select_client = new System.Windows.Forms.Button();
            this.debug_log_box = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.BackgroundColor = System.Drawing.SystemColors.Control;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.PID,
            this.账号,
            this.名字,
            this.服务器,
            this.等级,
            this.职业,
            this.血量,
            this.蓝量,
            this.金币,
            this.脚本,
            this.状态,
            this.开始,
            this.停止,
            this.编辑,
            this.删除});
            this.dataGridView1.Location = new System.Drawing.Point(6, 4);
            this.dataGridView1.Margin = new System.Windows.Forms.Padding(4);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.RowHeadersVisible = false;
            this.dataGridView1.RowTemplate.Height = 23;
            this.dataGridView1.Size = new System.Drawing.Size(1264, 142);
            this.dataGridView1.TabIndex = 0;
            // 
            // PID
            // 
            this.PID.HeaderText = "PID";
            this.PID.Name = "PID";
            this.PID.ReadOnly = true;
            this.PID.Width = 60;
            // 
            // 账号
            // 
            this.账号.HeaderText = "账号";
            this.账号.Name = "账号";
            this.账号.ReadOnly = true;
            // 
            // 名字
            // 
            this.名字.HeaderText = "名字";
            this.名字.Name = "名字";
            this.名字.ReadOnly = true;
            // 
            // 服务器
            // 
            this.服务器.HeaderText = "服务器";
            this.服务器.Name = "服务器";
            this.服务器.ReadOnly = true;
            this.服务器.Width = 120;
            // 
            // 等级
            // 
            this.等级.HeaderText = "等级";
            this.等级.Name = "等级";
            this.等级.ReadOnly = true;
            this.等级.Width = 60;
            // 
            // 职业
            // 
            this.职业.HeaderText = "职业";
            this.职业.Name = "职业";
            this.职业.ReadOnly = true;
            // 
            // 血量
            // 
            this.血量.HeaderText = "血量";
            this.血量.Name = "血量";
            this.血量.ReadOnly = true;
            this.血量.Width = 80;
            // 
            // 蓝量
            // 
            this.蓝量.HeaderText = "蓝量";
            this.蓝量.Name = "蓝量";
            this.蓝量.ReadOnly = true;
            this.蓝量.Width = 80;
            // 
            // 金币
            // 
            this.金币.HeaderText = "金币";
            this.金币.Name = "金币";
            this.金币.ReadOnly = true;
            this.金币.Width = 60;
            // 
            // 脚本
            // 
            this.脚本.HeaderText = "脚本";
            this.脚本.Name = "脚本";
            this.脚本.ReadOnly = true;
            this.脚本.Width = 130;
            // 
            // 状态
            // 
            this.状态.HeaderText = "状态";
            this.状态.Name = "状态";
            this.状态.ReadOnly = true;
            this.状态.Width = 130;
            // 
            // 开始
            // 
            this.开始.HeaderText = "开始";
            this.开始.Name = "开始";
            this.开始.ReadOnly = true;
            this.开始.Width = 60;
            // 
            // 停止
            // 
            this.停止.HeaderText = "停止";
            this.停止.Name = "停止";
            this.停止.ReadOnly = true;
            this.停止.Width = 60;
            // 
            // 编辑
            // 
            this.编辑.HeaderText = "编辑";
            this.编辑.Name = "编辑";
            this.编辑.ReadOnly = true;
            this.编辑.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.编辑.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.编辑.Text = "";
            this.编辑.Width = 60;
            // 
            // 删除
            // 
            this.删除.HeaderText = "删除";
            this.删除.Name = "删除";
            this.删除.ReadOnly = true;
            this.删除.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.删除.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.删除.Width = 60;
            // 
            // btn_Add
            // 
            this.btn_Add.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btn_Add.Location = new System.Drawing.Point(724, 200);
            this.btn_Add.Margin = new System.Windows.Forms.Padding(4);
            this.btn_Add.Name = "btn_Add";
            this.btn_Add.Size = new System.Drawing.Size(70, 30);
            this.btn_Add.TabIndex = 1;
            this.btn_Add.Text = "添加账号";
            this.btn_Add.UseVisualStyleBackColor = true;
            this.btn_Add.Click += new System.EventHandler(this.btn_Add_Click);
            // 
            // UpdateTimer
            // 
            this.UpdateTimer.Enabled = true;
            this.UpdateTimer.Tick += new System.EventHandler(this.UpdateTimer_Tick);
            // 
            // adjust_the_window
            // 
            this.adjust_the_window.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.adjust_the_window.Location = new System.Drawing.Point(802, 200);
            this.adjust_the_window.Margin = new System.Windows.Forms.Padding(4);
            this.adjust_the_window.Name = "adjust_the_window";
            this.adjust_the_window.Size = new System.Drawing.Size(70, 30);
            this.adjust_the_window.TabIndex = 3;
            this.adjust_the_window.Text = "调整窗口";
            this.adjust_the_window.UseVisualStyleBackColor = true;
            this.adjust_the_window.Click += new System.EventHandler(this.adjust_the_window_Click);
            // 
            // Start_all_processes
            // 
            this.Start_all_processes.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.Start_all_processes.Location = new System.Drawing.Point(724, 238);
            this.Start_all_processes.Margin = new System.Windows.Forms.Padding(4);
            this.Start_all_processes.Name = "Start_all_processes";
            this.Start_all_processes.Size = new System.Drawing.Size(90, 30);
            this.Start_all_processes.TabIndex = 4;
            this.Start_all_processes.Text = "启动所有进程";
            this.Start_all_processes.UseVisualStyleBackColor = true;
            this.Start_all_processes.Click += new System.EventHandler(this.Start_all_processes_Click);
            // 
            // Stop_all_processes
            // 
            this.Stop_all_processes.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.Stop_all_processes.Location = new System.Drawing.Point(860, 238);
            this.Stop_all_processes.Margin = new System.Windows.Forms.Padding(4);
            this.Stop_all_processes.Name = "Stop_all_processes";
            this.Stop_all_processes.Size = new System.Drawing.Size(90, 30);
            this.Stop_all_processes.TabIndex = 5;
            this.Stop_all_processes.Text = "停止所有进程";
            this.Stop_all_processes.UseVisualStyleBackColor = true;
            this.Stop_all_processes.Click += new System.EventHandler(this.Stop_all_processes_Click);
            // 
            // Server_Announcement
            // 
            this.Server_Announcement.AutoSize = true;
            this.Server_Announcement.Font = new System.Drawing.Font("微软雅黑", 11F);
            this.Server_Announcement.Location = new System.Drawing.Point(13, 246);
            this.Server_Announcement.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.Server_Announcement.Name = "Server_Announcement";
            this.Server_Announcement.Size = new System.Drawing.Size(466, 20);
            this.Server_Announcement.TabIndex = 6;
            this.Server_Announcement.Text = "仅支持1.12.1中文客户端、支持Turtle WoW 中文客户端 (Capybara)";
            this.Server_Announcement.Click += new System.EventHandler(this.Server_Announcement_Click);
            // 
            // Clear_list
            // 
            this.Clear_list.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.Clear_list.Location = new System.Drawing.Point(880, 200);
            this.Clear_list.Margin = new System.Windows.Forms.Padding(4);
            this.Clear_list.Name = "Clear_list";
            this.Clear_list.Size = new System.Drawing.Size(70, 30);
            this.Clear_list.TabIndex = 7;
            this.Clear_list.Text = "清空列表";
            this.Clear_list.UseVisualStyleBackColor = true;
            this.Clear_list.Click += new System.EventHandler(this.Clear_list_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.label1.Location = new System.Drawing.Point(13, 203);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(87, 17);
            this.label1.TabIndex = 10;
            this.label1.Text = "WoW 客户端: ";
            // 
            // Client_path
            // 
            this.Client_path.Enabled = false;
            this.Client_path.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.Client_path.Location = new System.Drawing.Point(102, 200);
            this.Client_path.Margin = new System.Windows.Forms.Padding(4);
            this.Client_path.Name = "Client_path";
            this.Client_path.ReadOnly = true;
            this.Client_path.Size = new System.Drawing.Size(314, 23);
            this.Client_path.TabIndex = 11;
            this.Client_path.Text = " 请选择一个WoW.exe客户端";
            // 
            // Select_client
            // 
            this.Select_client.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.Select_client.Location = new System.Drawing.Point(424, 200);
            this.Select_client.Margin = new System.Windows.Forms.Padding(4);
            this.Select_client.Name = "Select_client";
            this.Select_client.Size = new System.Drawing.Size(88, 25);
            this.Select_client.TabIndex = 12;
            this.Select_client.Text = "选择客户端";
            this.Select_client.UseVisualStyleBackColor = true;
            this.Select_client.Click += new System.EventHandler(this.Select_client_Click);
            // 
            // debug_log_box
            // 
            this.debug_log_box.AutoSize = true;
            this.debug_log_box.Location = new System.Drawing.Point(1171, 248);
            this.debug_log_box.Name = "debug_log_box";
            this.debug_log_box.Size = new System.Drawing.Size(99, 21);
            this.debug_log_box.TabIndex = 13;
            this.debug_log_box.Text = "开启调试日志";
            this.debug_log_box.UseVisualStyleBackColor = true;
            this.debug_log_box.CheckedChanged += new System.EventHandler(this.debug_log_box_CheckedChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("微软雅黑", 15F);
            this.label2.Location = new System.Drawing.Point(12, 160);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(112, 27);
            this.label2.TabIndex = 14;
            this.label2.Text = "客户端设置";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("微软雅黑", 15F);
            this.label3.Location = new System.Drawing.Point(719, 160);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(72, 27);
            this.label3.TabIndex = 15;
            this.label3.Text = "控制台";
            // 
            // Main
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1274, 275);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.debug_log_box);
            this.Controls.Add(this.Select_client);
            this.Controls.Add(this.Client_path);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Clear_list);
            this.Controls.Add(this.Server_Announcement);
            this.Controls.Add(this.Stop_all_processes);
            this.Controls.Add(this.Start_all_processes);
            this.Controls.Add(this.adjust_the_window);
            this.Controls.Add(this.btn_Add);
            this.Controls.Add(this.dataGridView1);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.Name = "Main";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Zero ";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button btn_Add;
        private System.Windows.Forms.Timer UpdateTimer;
        private System.Windows.Forms.Button adjust_the_window;
        private System.Windows.Forms.Button Start_all_processes;
        private System.Windows.Forms.Button Stop_all_processes;
        private System.Windows.Forms.Label Server_Announcement;
        private System.Windows.Forms.Button Clear_list;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Client_path;
        private System.Windows.Forms.Button Select_client;
        private System.Windows.Forms.CheckBox debug_log_box;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.DataGridViewTextBoxColumn PID;
        private System.Windows.Forms.DataGridViewTextBoxColumn 账号;
        private System.Windows.Forms.DataGridViewTextBoxColumn 名字;
        private System.Windows.Forms.DataGridViewTextBoxColumn 服务器;
        private System.Windows.Forms.DataGridViewTextBoxColumn 等级;
        private System.Windows.Forms.DataGridViewTextBoxColumn 职业;
        private System.Windows.Forms.DataGridViewTextBoxColumn 血量;
        private System.Windows.Forms.DataGridViewTextBoxColumn 蓝量;
        private System.Windows.Forms.DataGridViewTextBoxColumn 金币;
        private System.Windows.Forms.DataGridViewTextBoxColumn 脚本;
        private System.Windows.Forms.DataGridViewTextBoxColumn 状态;
        private System.Windows.Forms.DataGridViewButtonColumn 开始;
        private System.Windows.Forms.DataGridViewButtonColumn 停止;
        private System.Windows.Forms.DataGridViewButtonColumn 编辑;
        private System.Windows.Forms.DataGridViewButtonColumn 删除;
    }
}

