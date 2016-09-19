﻿using AircraftCarrierSlotSolver.Forms;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AircraftCarrierSlotSolver
{
	public partial class MainForm : Form
	{
		private List<AirCraft> _AirCraftList;
		private List<ShipInfo> _ShipInfoList;
		private DataGridViewComboBoxEditingControl dataGridViewComboBox = null;

		public MainForm()
		{
			InitializeComponent();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			var column = ShipSlotInfoDataGridView.Columns[0] as DataGridViewComboBoxColumn;

			using (var parser = new CsvParser(new StreamReader("air.csv", new UTF8Encoding(false))))
			{
				parser.Configuration.HasHeaderRecord = true;
				parser.Configuration.RegisterClassMap<AirCraftMap>();

				using (var reader = new CsvReader(parser))
				{
					_AirCraftList = reader.GetRecords<AirCraft>().ToList();
				}
			}

			using (var parser = new CsvParser(new StreamReader("slots.csv", new UTF8Encoding(false))))
			{
				parser.Configuration.HasHeaderRecord = true;
				parser.Configuration.RegisterClassMap<ShipInfoMap>();

				using (var reader = new CsvReader(parser))
				{
					_ShipInfoList = reader.GetRecords<ShipInfo>().ToList();
				}
			}

			column.DataSource = _ShipInfoList.Select(x => x.Name).ToList();
			ShipSelectComboBox.DataSource = _ShipInfoList.Select(x => x.Name).ToList();

			if (!File.Exists(Properties.Resources.SettingFileName))
			{
				Settings.Instance.AirCraftLimit = _AirCraftList.ToDictionary(x => x.Name, _ => 0).ConvertDictionaryToList();
				Settings.Instance.CruiserSlotNum = 1;
				Settings.SaveToXmlFile();
			}
		}

		private void AddButton_Click(object sender, EventArgs e)
		{
			this.shipSlotInfoBindingSource.Add(CreateShipSlotInfo(ShipSelectComboBox.SelectedValue.ToString()));
		}

		private void ShipSlotInfoDataGridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
		{
			//表示されているコントロールがDataGridViewComboBoxEditingControlか調べる
			if (e.Control is DataGridViewComboBoxEditingControl)
			{
				var dgv = sender as DataGridView;

				//該当する列か調べる
				if (dgv.CurrentCell.OwningColumn.DataPropertyName == "ShipName")
				{
					//編集のために表示されているコントロールを取得
					this.dataGridViewComboBox = e.Control as DataGridViewComboBoxEditingControl;
					//SelectedIndexChangedイベントハンドラを追加
					this.dataGridViewComboBox.SelectedIndexChanged += new EventHandler(dataGridViewComboBox_SelectedIndexChanged);
				}
			}
		}
		private void ShipSlotInfoDataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
		{
			//SelectedIndexChangedイベントハンドラを削除
			if (this.dataGridViewComboBox != null)
			{
				this.dataGridViewComboBox.SelectedIndexChanged -= new EventHandler(dataGridViewComboBox_SelectedIndexChanged);
				this.dataGridViewComboBox = null;
			}
		}


		//DataGridViewに表示されているコンボボックスの
		//SelectedIndexChangedイベントハンドラ
		private void dataGridViewComboBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			//選択されたアイテムを表示
			var rowIndex = ShipSlotInfoDataGridView.CurrentCell.RowIndex;
			var cb = sender as DataGridViewComboBoxEditingControl;
			var item = ShipSlotInfoDataGridView.Rows[rowIndex].DataBoundItem as ShipSlotInfo;
			var ship = _ShipInfoList.Where(x => x.Name == cb.SelectedItem?.ToString()).FirstOrDefault();
			if(ship == null)
			{
				return;
			}

			item.Slot1Num = ship.Slot1Num;
			item.Slot1 = string.Empty;
			item.Slot2Num = ship.Slot2Num;
			item.Slot2 = string.Empty;
			item.Slot3Num = ship.Slot3Num;
			item.Slot3 = string.Empty;
			item.Slot4Num = ship.Slot4Num;
			item.Slot4 = string.Empty;
			ShipSlotInfoDataGridView.InvalidateRow(rowIndex);
		}

		private ShipSlotInfo CreateShipSlotInfo(string name)
		{
			var ship = _ShipInfoList.Where(x => x.Name == name).First();
			return new ShipSlotInfo()
			{
				ShipName = ship.Name,
				Slot1Num = ship.Slot1Num,
				Slot2Num = ship.Slot2Num,
				Slot3Num = ship.Slot3Num,
				Slot4Num = ship.Slot4Num
			};
		}

		private void CalcButton_Click(object sender, EventArgs e)
		{
			var shipSlotList = new List<ShipSlotInfo>();

			using (StreamWriter writer = new StreamWriter(@"slot.lp", false, new UTF8Encoding(false)))
			{
				for (int i = 0; i< ShipSlotInfoDataGridView.Rows.Count; i++)
				{
					var item = ShipSlotInfoDataGridView.Rows[i].DataBoundItem as ShipSlotInfo;
					item.Slot1 = item.Slot2 = item.Slot3 = item.Slot4 = string.Empty;
					ShipSlotInfoDataGridView.InvalidateRow(i);
					shipSlotList.Add(item);
				}

				OutputTarget(writer, shipSlotList);

				OutputAirCondition(writer, shipSlotList);

				OutputSlotCondition(writer, shipSlotList);

				Settings.LoadFromXmlFile();

				OutputStockCondition(writer, shipSlotList, Settings.Instance.AirCraftLimit.ConvertListToDictionary());

				OutputShipTypeCondition(writer, shipSlotList);

				OutputBinary(writer, shipSlotList);

				writer.WriteLine("end");
			}

			var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

			using (StreamWriter writer = new StreamWriter(Path.Combine(dir, "solve.txt"), false, new UTF8Encoding(false)))
			{
				writer.WriteLine("read slot.lp");
				writer.WriteLine("optimize");
				writer.WriteLine("display solution");
				writer.WriteLine("quit");
			}

			var slotStringList = new List<string>();

			try
			{
				var logFile = Path.Combine(dir, "result.log");

				if(File.Exists(logFile))
				{
					File.Delete(logFile);
				}
				Settings.LoadFromXmlFile();

				var psi = new ProcessStartInfo();

				psi.FileName = Settings.Instance.SolverPath;
				psi.Arguments = "-b solve.txt" + " -l result.log";
				psi.WorkingDirectory = dir;

				var process = Process.Start(psi);

				process.WaitForExit();

				var log = Path.Combine(dir, "result.log");
				var regex = new System.Text.RegularExpressions.Regex(@"(?<slot>slot_\d+_\d_\d+).+");
				using (StreamReader r = new StreamReader(log))
				{
					string line;
					while ((line = r.ReadLine()) != null)
					{
						var matches = regex.Matches(line);
						if(matches.Count > 0)
						{
							slotStringList.Add(matches[0].Groups["slot"].Value);
						}
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("SCIPソルバーの実行に失敗しました。:" + ex.Message);

				return;
			}

			if(!slotStringList.Any())
			{
				MessageBox.Show("制空値を満たす解がありませんでした。");
				return;
			}

			foreach(var generatorInfo in slotStringList.Select(x => GetIEnumerable(shipSlotList).First(y => y.SlotName == x)))
			{
				var rowItem = GetRowItem(generatorInfo.Ship.Item1.Name);
				if( generatorInfo.Slot.Item2 == 0)
				{
					rowItem.Item1.Slot1 = generatorInfo.AirCraft.Item1.Name;
				}
				else if (generatorInfo.Slot.Item2 == 1)
				{
					rowItem.Item1.Slot2 = generatorInfo.AirCraft.Item1.Name;
				}
				else if (generatorInfo.Slot.Item2 == 2)
				{
					rowItem.Item1.Slot3 = generatorInfo.AirCraft.Item1.Name;
				}
				else if (generatorInfo.Slot.Item2 == 3)
				{
					rowItem.Item1.Slot4 = generatorInfo.AirCraft.Item1.Name;
				}

				ShipSlotInfoDataGridView.InvalidateRow(rowItem.Item2);
			}
		}

		private void OutputShipTypeCondition(StreamWriter writer, List<ShipSlotInfo> shipSlotList)
		{
			if(GetIEnumerable(shipSlotList).Any(x => x.Ship.Item1.Type == "空母"))
			{
				// 空母が1隻以上の場合
				// 空母に水上機載せない
				foreach (var record in GetIEnumerable(shipSlotList)
					.Where(x => x.Ship.Item1.Type == "空母" && 
						(x.AirCraft.Item1.Type == "水爆" || x.AirCraft.Item1.Type == "水戦")))
				{
					writer.WriteLine("+ " + record.SlotName);
				}
				writer.WriteLine("= 0");
				writer.WriteLine();
			}

			if (GetIEnumerable(shipSlotList).Any(x => x.Ship.Item1.Type == "巡洋艦"))
			{
				// 巡洋艦が1隻以上の場合
				// 巡洋艦に艦載機載せない
				foreach (var record in GetIEnumerable(shipSlotList)
					.Where(x => x.Ship.Item1.Type == "巡洋艦" &&
						(x.AirCraft.Item1.Type == "艦戦" || x.AirCraft.Item1.Type == "艦爆" || x.AirCraft.Item1.Type == "艦攻")))
				{
					writer.WriteLine("+ " + record.SlotName);
				}
				writer.WriteLine("= 0");
				writer.WriteLine();

				Settings.LoadFromXmlFile();

				foreach(var noEquipShipList in GetIEnumerable(shipSlotList)
				   .Where(x => x.Ship.Item1.Type == "巡洋艦" && x.AirCraft.Item1.Name == "装備なし")
				   .GroupBy(y => y.Ship.Item2))
				{
					foreach(var noEquipList in noEquipShipList)
					{
						writer.WriteLine("+ " + noEquipList.SlotName);
					}

					writer.WriteLine(">= " + (4 - Settings.Instance.CruiserSlotNum));
					writer.WriteLine();
				}
			}

		}

		private Tuple<ShipSlotInfo, int> GetRowItem(string shipName)
		{
			for(int i = 0; i < ShipSlotInfoDataGridView.Rows.Count; i++)
			{
				var item = ShipSlotInfoDataGridView.Rows[i].DataBoundItem as ShipSlotInfo;
				if(item.ShipName == shipName)
				{
					return Tuple.Create(item, i);
				}
			}

			throw new Exception("艦名が見つかりません");
		}

		private void OutputStockCondition(StreamWriter writer, List<ShipSlotInfo> shipSlotList, Dictionary<string, int> condition)
		{
			foreach(var dic in condition)
			{
				foreach (var record in GetIEnumerable(shipSlotList).Where(x => x.AirCraft.Item1.Name == dic.Key))
				{
					writer.WriteLine("+ " + record.SlotName);
				}
				writer.WriteLine("<= " + dic.Value);
				writer.WriteLine();
			}

		}

		private void OutputBinary(StreamWriter writer, List<ShipSlotInfo> shipSlotList)
		{
			writer.WriteLine("binary");
			foreach (var record in GetIEnumerable(shipSlotList))
			{
				writer.WriteLine(record.SlotName + @" \ " + record.Ship.Item1.Name + " " + record.Slot.Item1 + " " + record.AirCraft.Item1.Name);
			}
			writer.WriteLine();

		}

		private void OutputTarget(StreamWriter writer, List<ShipSlotInfo> shipSlotList)
		{
			writer.WriteLine("maximize");

			foreach(var record in GetIEnumerable(shipSlotList))
			{
				if(record.AirCraft.Item1.Type == "艦爆" || record.AirCraft.Item1.Type == "艦攻")
				{
					var text = "+ " + record.Power + " " + record.SlotName + @" \ " + record.Ship.Item1.Name + " " + record.Slot.Item1 + " " + record.AirCraft.Item1.Name;
					writer.WriteLine(text);
				}
			}

			writer.WriteLine();
		}

		private void OutputAirCondition(StreamWriter writer, List<ShipSlotInfo> shipSlotList)
		{
			writer.WriteLine("subject to");

			foreach (var record in GetIEnumerable(shipSlotList))
			{
				var text = "+ " + record.AirSuperiorityPotential + " " + record.SlotName + @" \ " + record.Ship.Item1.Name + " " + record.Slot.Item1 + " " + record.AirCraft.Item1.Name;
				writer.WriteLine(text);
			}
			writer.WriteLine(">= " + AirSuperiorityNumericUpDown.Value);
			writer.WriteLine();
		}

		private void OutputSlotCondition(StreamWriter writer, List<ShipSlotInfo> shipSlotList)
		{
			foreach(var group in GetIEnumerable(shipSlotList).GroupBy(x => new {Ship = x.Ship.Item2, Slot = x.Slot.Item2}))
			{
				foreach(var g in group)
				{
					var text = "+ " + g.SlotName;
					writer.WriteLine(text);
				}
				writer.WriteLine("= 1");
				writer.WriteLine();
			}
		}

		private IEnumerable<GeneratorInfo> GetIEnumerable(List<ShipSlotInfo> shipSlotList)
		{
			var noEquip = new List<AirCraft>()
			{
				new AirCraft() { Name = "装備なし", Type = "水戦", AA = 0, FirePower = 0, Bomber = 0, Torpedo = 0, Evasion = 0, Accuracy = 0 }
			};	

			foreach (var ship in _ShipInfoList
				.Select((item, index) => Tuple.Create(item, index))
				.Where(x => shipSlotList.Select(y => y.ShipName).Contains(x.Item1.Name)))
			{
				foreach (var slot in ship.Item1.Slots.Select((item, index) => Tuple.Create(item, index)))
				{
					foreach (var aircraft in _AirCraftList.Concat(noEquip).Select((item , index) => Tuple.Create(item,index)))
					{
						yield return new GeneratorInfo() { Ship = ship, Slot = slot, AirCraft = aircraft };
					}
				}
			}
		}

		private void airCraftSettingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var form = new AirCraftSettingForm();
			form.ShowDialog();
		}

		private void solverSettingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var ofd = new OpenFileDialog())
			{
				//[ファイルの種類]に表示される選択肢を指定する
				//指定しないとすべてのファイルが表示される
				ofd.Filter = "実行ファイル|*.exe";
				//[ファイルの種類]ではじめに選択されるものを指定する
				//2番目の「すべてのファイル」が選択されているようにする
				ofd.FilterIndex = 1;
				//タイトルを設定する
				ofd.Title = "開くファイルを選択してください";
				//ダイアログボックスを閉じる前に現在のディレクトリを復元するようにする
				ofd.RestoreDirectory = true;
				//存在しないファイルの名前が指定されたとき警告を表示する
				//デフォルトでTrueなので指定する必要はない
				ofd.CheckFileExists = true;
				//存在しないパスが指定されたとき警告を表示する
				//デフォルトでTrueなので指定する必要はない
				ofd.CheckPathExists = true;

				// ダイアログを表示し、戻り値が [OK] の場合は、選択したファイルを表示する
				if (ofd.ShowDialog() == DialogResult.OK)
				{
					Settings.LoadFromXmlFile();

					Settings.Instance.SolverPath = ofd.FileName;

					Settings.SaveToXmlFile();
				}
			}

		}
	}
}
