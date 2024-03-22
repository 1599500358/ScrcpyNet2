using ReactiveUI;
using ScrcpyNet.Sample.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// ScrcpyItem.xaml 的交互逻辑
    /// </summary>
    public partial class ScrcpyItem : ReactiveUserControl<ScrcpyViewModel>
    {
        public ScrcpyItem()
        {
            InitializeComponent();
        }
    }
}
