using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace influx2Exporter.Views.Controls
{
 public partial class DateTimePicker : UserControl
 {
 public DateTimePicker()
 {
 InitializeComponent();
 Loaded += OnLoaded;
 }

 private void OnLoaded(object sender, RoutedEventArgs e)
 {
 if (PART_Hour.Items.Count ==0)
 {
 for (int i =0; i <24; i++) PART_Hour.Items.Add(i.ToString("00"));
 for (int i =0; i <60; i++) { PART_Minute.Items.Add(i.ToString("00")); PART_Second.Items.Add(i.ToString("00")); }
 UpdateInputs();
 }
 }

 public DateTime? Value
 {
 get => (DateTime?)GetValue(ValueProperty);
 set => SetValue(ValueProperty, value);
 }
 public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
 nameof(Value), typeof(DateTime?), typeof(DateTimePicker), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

 private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
 if (d is DateTimePicker p) p.UpdateInputs();
 }

 private void UpdateInputs()
 {
 if (!IsLoaded) return;
 if (Value.HasValue)
 {
 var v = Value.Value;
 PART_Date.SelectedDate = v.Date;
 PART_Hour.SelectedItem = v.Hour.ToString("00");
 PART_Minute.SelectedItem = v.Minute.ToString("00");
 PART_Second.SelectedItem = v.Second.ToString("00");
 }
 else
 {
 PART_Date.SelectedDate = null;
 PART_Hour.SelectedIndex = -1;
 PART_Minute.SelectedIndex = -1;
 PART_Second.SelectedIndex = -1;
 }
 }

 private void OnDateChanged(object sender, SelectionChangedEventArgs e) => Compose();
 private void OnTimePartChanged(object sender, SelectionChangedEventArgs e) => Compose();

 private void Compose()
 {
 if (!IsLoaded) return;
 if (PART_Date.SelectedDate is DateTime d && PART_Hour.SelectedItem is string hh && PART_Minute.SelectedItem is string mm && PART_Second.SelectedItem is string ss)
 {
 int h = int.Parse(hh, CultureInfo.InvariantCulture);
 int m = int.Parse(mm, CultureInfo.InvariantCulture);
 int s = int.Parse(ss, CultureInfo.InvariantCulture);
 Value = new DateTime(d.Year, d.Month, d.Day, h, m, s, DateTimeKind.Local);
 }
 }
 }
}
