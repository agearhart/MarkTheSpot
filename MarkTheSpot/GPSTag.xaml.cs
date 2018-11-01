using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

using System.Text;

namespace MarkTheSpot
{
    public partial class GPSTag : UserControl
    {
        public static char Delimiter = (char)230;//nu
        //public vars
        public Double TagLat { get; set; }
        public Double TagLng { get; set; }
        public string TagName { get; set; }
        public Guid Id { get; set; }

        //private vars
        RemoveDelegate removeDel;
        FindDelegate findDel;

        public GPSTag()
        {
            InitializeComponent();
        }

        //delegates
        public delegate void FindDelegate(GPSTag b);
        public delegate void RemoveDelegate(Guid id);

        public GPSTag(Guid id, Double lat, double lng, string name, RemoveDelegate rd, FindDelegate fd)
        {
            InitializeComponent();

            this.Id = id;
            this.TagLat = lat;
            this.TagLng = lng;
            this.TagName = name; //This should set the button's name

            FindBtnTxt.Text = name;

            removeDel = rd;
            findDel = fd;
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete " + TagName + "?", "Delete "+TagName, MessageBoxButton.OKCancel);

            if ( result == MessageBoxResult.OK && removeDel != null)
            {
                removeDel(Id);
            }
        }

        private void FindBtn_Click(object sender, RoutedEventArgs e)
        {
            if (findDel != null)
            {
                findDel(this);
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(TagLat);
            sb.Append(Delimiter);

            sb.Append(TagLng);
            sb.Append(Delimiter);

            sb.Append(TagName);
            sb.Append(Delimiter);

            sb.Append(Id);
            sb.Append(Delimiter);

            return sb.ToString();
        }
    }
}
