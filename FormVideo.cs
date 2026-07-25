using System;
using System.Windows.Forms;

namespace ccd_helper
{
    public class FormVideo : Form
    {
        public FormVideo()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.ShowInTaskbar = false;
            this.Enabled = true;            // 必须为 true，否则无法接收鼠标
            this.TabStop = false;
            this.SetStyle(ControlStyles.Selectable, false);
            this.SetStyle(ControlStyles.ContainerControl, false);
            this.TopMost = false;

            // 不设置 WS_EX_TRANSPARENT，让窗口正常接收鼠标消息
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 移除 WS_EX_TRANSPARENT（如有继承），确保鼠标可穿透
                // 但这里我们不设置，保持默认
                return cp;
            }
        }

        // 阻止窗口被鼠标激活
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 0x0003;

            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        // 可选：重写 OnMouseClick 以触发自定义事件，但我们将直接使用 MouseClick 事件
        protected override void OnPaint(PaintEventArgs e) { }
    }
}