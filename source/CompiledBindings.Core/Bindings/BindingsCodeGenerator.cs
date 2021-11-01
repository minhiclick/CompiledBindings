﻿using System.ComponentModel;
using System.Linq;
using System.Text;
using Mono.Cecil;

#nullable enable

namespace CompiledBindings
{
	public class BindingsCodeGenerator : XamlCodeGenerator
	{
		public BindingsCodeGenerator(string langVersion) : base(langVersion)
		{
		}

		public void GenerateCode(StringBuilder output, BindingsData bindingsData, string? targetNamespace, string targetClassName, string? declaringType = null, string? nameSuffix = null, bool @interface = false, bool generateCodeAttr = false)
		{
			if (targetClassName == null)
			{
				targetClassName = bindingsData.TargetRootType!.Type.Name;
				targetNamespace = bindingsData.TargetRootType!.Type.Namespace;
			}

			#region Namespace Begin

			if (targetNamespace != null)
			{
				output.AppendLine(
$@"namespace {targetNamespace}
{{");
			}

			#endregion

			#region Usings

			output.AppendLine(
$@"	using System;
	using System.ComponentModel;
	using System.Globalization;");

			foreach (string ns in bindingsData.Bindings.SelectMany(b => b.Property.IncludeNamespaces).Distinct())
			{
				output.AppendLine(
$@"	using {ns};");
			}

			#endregion

			#region Parent Class Begin

			if (declaringType != null)
			{
				output.AppendLine(
$@"	
	partial class {declaringType}
	{{");
			}

			output.AppendLine();

			if (generateCodeAttr)
			{
				output.AppendLine(
$@"	[System.CodeDom.Compiler.GeneratedCode(""CompiledBindings"", null)]");
			}

			output.AppendLine(
$@"	partial class {targetClassName}
	{{");

			#endregion

			GenerateBindingsClass(output, bindingsData, targetNamespace, targetClassName, declaringType, nameSuffix, @interface, generateCodeAttr);

			#region Parent Class End

			if (declaringType != null)
			{
				output.AppendLine(
$@"	}}");
			}

			output.AppendLine(
$@"	}}");
			#endregion

			#region Namespace End

			if (targetNamespace != null)
			{
				output.AppendLine(
$@"}}");
			}

			#endregion
		}

		public void GenerateBindingsClass(StringBuilder output, BindingsData bindingsData, string? targetNamespace, string targetClassName, string? declaringType = null, string? nameSuffix = null, bool @interface = false, bool generateCodeAttr = false)
		{
			bool isDiffDataRoot = targetClassName != bindingsData.DataRootType.Type.Name || targetNamespace != bindingsData.DataRootType.Type.Namespace;
			var rootGroup = bindingsData.NotifyPropertyChangedList.SingleOrDefault(g => g.SourceExpression is ParameterExpression pe && pe.Name == "dataRoot");

			var iNotifyPropertyChangedType = TypeInfoUtils.GetTypeThrow(typeof(INotifyPropertyChanged));

			#region Class Begin

			if (@interface)
			{
				output.AppendLine(
$@"		void InitializeBeforeConstructor{nameSuffix}()
		{{
			Bindings{nameSuffix} = new {targetClassName}_Bindings{nameSuffix}();
		}}
");
			}
			else
			{
				output.AppendLine(
$@"		{targetClassName}_Bindings{nameSuffix} Bindings{nameSuffix} = new {targetClassName}_Bindings{nameSuffix}();
");
			}

			output.Append(
$@"		class {targetClassName}_Bindings{nameSuffix}");

			if (@interface)
			{
				output.Append($@" : I{targetClassName}_Bindings{nameSuffix}");
			}

			output.AppendLine(
$@"
		{{");

			#endregion

			#region Fields declaration

			output.AppendLine(
$@"			{targetClassName} _targetRoot;");

			if (isDiffDataRoot)
			{
				output.AppendLine(
$@"			global::{ bindingsData.DataRootType.Type.GetCSharpFullName()} _dataRoot;");
			}

			if (bindingsData.NotifyPropertyChangedList.Count > 0)
			{
				output.AppendLine(
$@"			{targetClassName}_BindingsTrackings{nameSuffix} _bindingsTrackings;");
			}

			// Generate _eventHandlerXXX fields for event bindings
			foreach (var binding in bindingsData.Bindings.Where(b => b.Property.TargetEvent != null))
			{
				output.AppendLine(
$@"			global::{binding.Property.TargetEvent!.EventType.GetCSharpFullName()} _eventHandler{binding.Index};");
			}

			// Generate flags for two-way bindings
			foreach (var bind in bindingsData.Bindings.Where(b => b.Mode == BindingMode.TwoWay))
			{
				output.AppendLine(
$@"			bool _settingBinding{bind.Index};");
			}

			GenerateBindingsExtraFieldDeclarations(output, bindingsData);

			#endregion // Fields declaration

			#region Initialize Method

			// Create Initialize method
			if (isDiffDataRoot)
			{
				output.AppendLine(
$@"
			public void Initialize({targetClassName} targetRoot, global::{bindingsData.DataRootType.Type.GetCSharpFullName()} dataRoot)
			{{
				if (_targetRoot != null)
					throw new System.InvalidOperationException();
				if (targetRoot == null)
					throw new System.ArgumentNullException(nameof(targetRoot));");
				if (bindingsData.DataRootType.Type.IsNullable())
				{
					output.AppendLine(
$@"				if (dataRoot == null)
					throw new System.ArgumentNullException(nameof(dataRoot));");
				}

				output.AppendLine(
$@"
				_targetRoot = targetRoot;
				_dataRoot = dataRoot;");
			}
			else
			{
				output.AppendLine(
$@"
			public void Initialize({targetClassName} dataRoot)
			{{
				if (_targetRoot != null)
					throw new System.InvalidOperationException();
				if (dataRoot == null)
					throw new System.ArgumentNullException(nameof(dataRoot));

				_targetRoot = dataRoot;");
			}

			if (bindingsData.NotifyPropertyChangedList.Count > 0)
			{
				output.AppendLine(
$@"				_bindingsTrackings = new {targetClassName}_BindingsTrackings{nameSuffix}(this);");
			}

			output.AppendLine(
$@"
				Update();");

			// Generate setting PropertyChanged event handler for data root
			if (rootGroup != null)
			{
				int index = bindingsData.NotifyPropertyChangedList.IndexOf(rootGroup);
				output.AppendLine(
$@"
				_bindingsTrackings.SetPropertyChangedEventHandler{index}(dataRoot);");
			}

			// Set event handlers for two-way bindings
			if (bindingsData.TwoWayEvents.Count > 0)
			{
				output.AppendLine();
				foreach (var ev in bindingsData.TwoWayEvents)
				{
					var first = ev.Bindings[0];
					string targetExpr = "_targetRoot";
					if (first.Property.Object.Name != null)
					{
						targetExpr += "." + first.Property.Object.Name;
					}

					if (first.IsDPChangeEvent)
					{
						GenerateSetDependencyPropertyChangedCallback(output, ev, targetExpr);
					}
					else
					{
						targetExpr += "." + first.TargetChangedEvent!.Name;

						output.AppendLine(
$@"				{targetExpr} += OnTargetChanged{ev.Index};");
					}
				}
			}

			// Close Initialize method
			output.AppendLine(
$@"			}}");

			#endregion Initialize Method

			#region Cleanup Method

			// Generate Cleanup method
			output.AppendLine(
$@"
			public void Cleanup()
			{{");

			output.AppendLine(
$@"				if (_targetRoot != null)
				{{");

			// Unset event handlers for two-way bindings
			foreach (var ev in bindingsData.TwoWayEvents)
			{
				var first = ev.Bindings[0];
				string targetExpr = "_targetRoot";
				if (first.Property.Object.Name != null)
				{
					targetExpr += "." + first.Property.Object.Name;
				}

				if (first.IsDPChangeEvent)
				{
					GenerateUnsetDependencyPropertyChangedCallback(output, ev, targetExpr);
				}
				else
				{
					targetExpr += "." + first.TargetChangedEvent!.Name;
					output.AppendLine(
$@"					{targetExpr} -= OnTargetChanged{ev.Index};");
				}
			}

			// Unset event handlers for event bindings
			foreach (var binding in bindingsData.Bindings.Where(b => b.Property.TargetEvent != null))
			{
				string targetExpr = "_targetRoot";
				if (binding.Property.Object.Name != null)
				{
					targetExpr += $".{binding.Property.Object.Name}";
				}
				targetExpr += $".{binding.Property.MemberName}";
				output.AppendLine(
$@"					if (_eventHandler{binding.Index} != null)
					{{
						{targetExpr} -= _eventHandler{binding.Index};
						_eventHandler{binding.Index} = null;
					}}");
			}

			if (bindingsData.NotifyPropertyChangedList.Count > 0)
			{
				output.AppendLine(
$@"					_bindingsTrackings.Cleanup();");
			}

			if (isDiffDataRoot && bindingsData.DataRootType.Type.IsNullable())
			{
				output.AppendLine(
$@"					_dataRoot = null;");
			}

			output.AppendLine(
$@"					_targetRoot = null;
				}}
			}}");

			#endregion Cleanup Method

			#region Update Method

			// Generate Update method
			output.AppendLine(
$@"
			public void Update()
			{{
				if (_targetRoot == null)
				{{
					throw new System.InvalidOperationException();
				}}

				var targetRoot = _targetRoot;
				var dataRoot = {(isDiffDataRoot ? "_dataRoot" : "_targetRoot")};");
			GenerateUpdateMethodBody(output, bindingsData.UpdateMethod, targetRootVariable: "targetRoot", align: "\t");

			output.AppendLine();

			foreach (var group in bindingsData.NotifyPropertyChangedList.Where(g => g != rootGroup))
			{
				output.AppendLine(
$@"				_bindingsTrackings.SetPropertyChangedEventHandler{group.Index}({group.SourceExpression});");
			}

			// Close Update method
			output.AppendLine(
$@"			}}");

			#endregion

			#region UpdateSourceOfExplicitTwoWayBindings

			var explicitBindings = bindingsData.Bindings.Where(b => b.Mode is BindingMode.TwoWay or BindingMode.OneWayToSource && b.UpdateSourceTrigger == UpdateSourceTrigger.Explicit);
			if (@interface || explicitBindings.Any())
			{
				output.AppendLine(
$@"
			public void UpdateSourceOfExplicitTwoWayBindings()
			{{
				var dataRoot = {(isDiffDataRoot ? "_dataRoot" : "_targetRoot")};");

				foreach (var bind in explicitBindings)
				{
					GenerateSetSource(bind, null);
				}

				output.AppendLine(
$@"			}}");
			}

			#endregion

			#region OnTargetChanged methods

			foreach (var ev in bindingsData.TwoWayEvents)
			{
				output.AppendLine();

				var first = ev.Bindings[0];
				if (first.IsDPChangeEvent)
				{
					GenerateDependencyPropertyChangedCallback(output, $"OnTargetChanged{ev.Index}");
				}
				else
				{
					output.AppendLine(
$@"			private void OnTargetChanged{ev.Index}({string.Join(", ", ev.Bindings[0].TargetChangedEvent!.GetEventHandlerParameterTypes().Select((t, i) => $"global::{t.GetCSharpFullName()} p{i}"))})");
				}

				output.AppendLine(
$@"			{{
				var dataRoot = {(isDiffDataRoot ? "_dataRoot" : "_targetRoot")};
				var targetRoot = _targetRoot;");

				if (first.TargetChangedEvent?.Name == "PropertyChanged")
				{
					output.AppendLine(
$@"				switch (p1.PropertyName)
				{{");
					foreach (var group in ev.Bindings.Where(b => b.Property.TargetProperty != null).GroupBy(b => b.Property.MemberName))
					{
						output.AppendLine(
$@"					case ""{group.Key}"":");
						foreach (var bind in group)
						{
							GenerateSetSource(bind, "\t\t");
						}
						output.AppendLine(
$@"						break;");
					}
					output.AppendLine(
$@"				}}");
				}
				else
				{
					foreach (var bind in ev.Bindings)
					{
						GenerateSetSource(bind, null);
					}
				}

				output.AppendLine(
$@"			}}");
			}

			#endregion

			#region Trackings Class

			if (bindingsData.NotifyPropertyChangedList.Count > 0)
			{
				#region Tracking Class Begin

				output.AppendLine(
$@"
			class {targetClassName}_BindingsTrackings{nameSuffix}
			{{");

				#endregion

				#region Fields

				output.AppendLine(
$@"				global::System.WeakReference _bindingsWeakRef;");

				// Generate _propertyChangeSourceXXX fields
				foreach (var group in bindingsData.NotifyPropertyChangedList)
				{
					output.AppendLine(
$@"				global::{group.SourceExpression.Type.Type.GetCSharpFullName()} _propertyChangeSource{group.Index};");
				}

				GenerateTrackingsExtraFieldDeclarations(output, bindingsData);

				#endregion

				#region Constructor

				output.AppendLine(
$@"
				public {targetClassName}_BindingsTrackings{nameSuffix}({targetClassName}_Bindings{nameSuffix} bindings)
				{{
					_bindingsWeakRef = new global::System.WeakReference(bindings);
				}}");

				#endregion

				#region Cleanup Method

				output.AppendLine(
$@"
				public void Cleanup()
				{{");

				// Unset property changed event handlers
				foreach (var group in bindingsData.NotifyPropertyChangedList)
				{
					output.AppendLine(
$@"					if (_propertyChangeSource{group.Index} != null)
					{{");
					GenerateUnsetPropertyChangedEventHandler(group, $"_propertyChangeSource{group.Index}", "\t");
					output.AppendLine(
$@"						_propertyChangeSource{group.Index} = null;
					}}");
				}

				// Close the Cleanup method
				output.AppendLine(
$@"				}}");

				#endregion

				#region SetPropertyChangedEventHandler methods

				foreach (var notifyGroup in bindingsData.NotifyPropertyChangedList)
				{
					string cacheVar = "_propertyChangeSource" + notifyGroup.Index;
					output.AppendLine(
$@"
				public void SetPropertyChangedEventHandler{notifyGroup.Index}(global::{notifyGroup.SourceExpression.Type.Type.GetCSharpFullName()} value)
				{{
					if ({cacheVar} != null && !object.ReferenceEquals({cacheVar}, value))
					{{");
					GenerateUnsetPropertyChangedEventHandler(notifyGroup, cacheVar, "\t");
					output.AppendLine(
$@"						{cacheVar} = null;
					}}
					if ({cacheVar} == null && value != null)
					{{
						{cacheVar} = value;");
					GenerateSetPropertyChangedEventHandler(notifyGroup, cacheVar, "\t");
					output.AppendLine(
$@"					}}
				}}");
				}

				#endregion

				#region OnPropertyChange methods

				foreach (var notifyGroup in bindingsData.NotifyPropertyChangedList)
				{
					if (iNotifyPropertyChangedType.IsAssignableFrom(notifyGroup.SourceExpression.Type))
					{
						output.AppendLine(
$@"
				private void OnPropertyChanged{notifyGroup.Index}(object sender, System.ComponentModel.PropertyChangedEventArgs e)
				{{");
						output.AppendLine(
$@"					var bindings = TryGetBindings();
					if (bindings == null)
					{{
						return;
					}}

					var targetRoot = bindings._targetRoot;
					var dataRoot = bindings.{(isDiffDataRoot ? "_dataRoot" : "_targetRoot")};
					var typedSender = (global::{notifyGroup.SourceExpression.Type.Type.GetCSharpFullName()})sender;
					var notifyAll = string.IsNullOrEmpty(e.PropertyName);
");

						for (int i = 0; i < notifyGroup.Properties.Count; i++)
						{
							var prop = notifyGroup.Properties[i];
							output.AppendLine(
$@"					if (notifyAll || e.PropertyName == ""{prop.Property.Definition.Name}"")
					{{");
							GenerateUpdateMethodBody(output, prop.UpdateMethod, targetRootVariable: "targetRoot", bindingsAccess: "bindings.", align: "\t\t\t");
							foreach (var dependentGroup in prop.DependentNotifyProperties)
							{
								output.AppendLine(
$@"						SetPropertyChangedEventHandler{dependentGroup.Index}({dependentGroup.SourceExpression});");
							}

							output.AppendLine(
$@"						if (!notifyAll)
						{{
							return;
						}}
					}}");
						}
						output.AppendLine(
$@"				}}");

					}
					else
					{
						foreach (var prop in notifyGroup.Properties)
						{
							output.AppendLine();
							GenerateDependencyPropertyChangedCallback(output, $"OnPropertyChanged{notifyGroup.Index}_{prop.Property.Definition.Name}", "\t");
							output.AppendLine(
$@"				{{");
							output.AppendLine(
$@"					var bindings = TryGetBindings();
					if (bindings == null)
					{{
						return;
					}}

					var targetRoot = bindings._targetRoot;
					var dataRoot = bindings.{(isDiffDataRoot ? "_dataRoot" : "_targetRoot")};
					var typedSender = (global::{notifyGroup.SourceExpression.Type.Type.GetCSharpFullName()})sender;");

							GenerateUpdateMethodBody(output, prop.UpdateMethod, targetRootVariable: "targetRoot", bindingsAccess: "bindings.", align: "\t\t");
							foreach (var dependentGroup in prop.DependentNotifyProperties)
							{
								output.AppendLine(
$@"					SetPropertyChangedEventHandler{dependentGroup.Index}({dependentGroup.SourceExpression});");
							}
							output.AppendLine(
	$@"				}}");
						}
					}
				}

				#endregion

				#region TryGetBindings method

				output.AppendLine(
$@"
				{targetClassName}_Bindings{nameSuffix} TryGetBindings()
				{{
					{targetClassName}_Bindings{nameSuffix} bindings = null;
					if (_bindingsWeakRef != null)
					{{
						bindings = ({targetClassName}_Bindings{nameSuffix})_bindingsWeakRef.Target;
						if (bindings == null)
						{{
							_bindingsWeakRef = null;
							Cleanup();
						}}
					}}
					return bindings;
				}}");

				#endregion

				#region Tracking Class End

				output.AppendLine(
$@"			}}");
				#endregion
			}

			#endregion // Trackings Class

			#region Class End

			output.AppendLine(
$@"		}}");

			#endregion

			#region Local Functions

			void GenerateSetPropertyChangedEventHandler(NotifyPropertyChangedData notifyGroup, string cacheVar, string? a)
			{
				if (iNotifyPropertyChangedType.IsAssignableFrom(notifyGroup.SourceExpression.Type))
				{
					output.AppendLine(
$@"{a}					((System.ComponentModel.INotifyPropertyChanged){cacheVar}).PropertyChanged += OnPropertyChanged{notifyGroup.Index};");
				}
				else
				{
					foreach (var notifyProp in notifyGroup.Properties)
					{
						GenerateRegisterDependencyPropertyChangeEvent(output, notifyGroup, notifyProp, cacheVar, $"OnPropertyChanged{notifyGroup.Index}_{notifyProp.Property.Definition.Name}");
					}
				}
			}

			void GenerateUnsetPropertyChangedEventHandler(NotifyPropertyChangedData notifyGroup, string cacheVar, string? a)
			{
				if (iNotifyPropertyChangedType.IsAssignableFrom(notifyGroup.SourceExpression.Type))
				{
					output.AppendLine(
$@"{a}					((System.ComponentModel.INotifyPropertyChanged){cacheVar}).PropertyChanged -= OnPropertyChanged{notifyGroup.Index};");
				}
				else
				{
					foreach (var notifyProp in notifyGroup.Properties)
					{
						GenerateUnregisterDependencyPropertyChangeEvent(output, notifyGroup, notifyProp, cacheVar, $"OnPropertyChanged{notifyGroup.Index}_{notifyProp.Property.Definition.Name}");
					}
				}
			}

			void GenerateSetSource(Bind bind, string? a)
			{
				var expr = bind.BindBackExpression ?? bind.Expression!;
				var me = expr as MemberExpression;

				string memberExpr = "_targetRoot";
				if (bind.Property.Object.Name != null)
				{
					memberExpr += "." + bind.Property.Object.Name;
				}

				TypeInfo sourceType;
				if (me?.Member is MethodDefinition)
				{
					sourceType = new TypeInfo(((MethodDefinition)me.Member).Parameters.Last().ParameterType.ResolveEx()!);
				}
				else
				{
					sourceType = expr.Type;
				}

				var setExpr = memberExpr + "." + bind.Property.MemberName;
				if (bind.Converter != null)
				{
					string sourceTypeFullName = sourceType.Type.GetCSharpFullName();
					string? cast = sourceTypeFullName == "System.Object" ? null : $"(global::{sourceTypeFullName})";
					string parameter = bind.ConverterParameter?.ToString() ?? "null";
					setExpr = GenerateConvertBackCall(bind.Converter, setExpr, sourceTypeFullName, parameter, cast);
				}
				else if (sourceType.Type.FullName == "System.String" && bind.Property.MemberType.Type.FullName != "System.String")
				{
					if (bind.Property.MemberType.IsNullable)
					{
						setExpr += "?";
					}
					setExpr += ".ToString()";
				}
				else if (!sourceType.IsAssignableFrom(bind.Property.MemberType))
				{
					var typeName = $"global::{sourceType.Type.GetCSharpFullName()}";
					if (bind.Property.MemberType.Type.FullName == "System.Object" && !sourceType.Type.IsValueType)
					{
						setExpr = $"({typeName}){setExpr}";
					}
					else
					{
						setExpr = $"({typeName})System.Convert.ChangeType({setExpr}, typeof({typeName}), null)";
					}
				}

				bool isNullable = me?.Expression.IsNullable == true;

				string? a2 = a;
				if (bind.Mode == BindingMode.TwoWay)
				{
					output.AppendLine(
$@"{a}				if (!_settingBinding{bind.Index})
{a}				{{
{a}					_settingBinding{bind.Index} = true;");
					a2 = a + '\t';
				}
				output.AppendLine(
$@"{a2}				try
{a2}				{{");
				if (isNullable)
				{
					output.AppendLine(
$@"{a2}					var value = {me!.Expression};
{a2}					if (value != null)
{a2}					{{");
					GenerateSetTarget($"value.{me.Member.Name}", a2 + '\t');
					output.AppendLine(
$@"{a2}					}}");
				}
				else
				{
					GenerateSetTarget(expr.ToString(), a2);
				}
				output.AppendLine(
$@"{a2}				}}
{a2}				catch
{a2}				{{
{a2}				}}");
				if (bind.Mode == BindingMode.TwoWay)
				{
					output.AppendLine(
$@"{a}					finally
{a}					{{
{a}						_settingBinding{bind.Index} = false;
{a}					}}
{a}				}}");
				}

				void GenerateSetTarget(string expression, string? a)
				{
					if (me?.Member is MethodDefinition)
					{
						output.AppendLine(
$@"{a}					{expression}({setExpr});");
					}
					else
					{
						output.AppendLine(
$@"{a}					{expression} = {setExpr};");
					}
				}
			}

			#endregion
		}

		public void GenerateBindingsInterface(StringBuilder output, string dataTypeFullName, string? targetClassName = null, string? targetNamespace = null, string? declaringType = null, string? nameSuffix = null)
		{
			string? dataTypeParam = null;
			if (dataTypeFullName != null)
			{
				dataTypeParam = $", global::{dataTypeFullName} dataRoot";
			}

			if (targetNamespace != null)
			{
				output.AppendLine(
$@"namespace {targetNamespace}
{{");
			}


			if (declaringType != null)
			{
				output.AppendLine(
$@"	
	partial class {declaringType}
	{{");
			}

			output.AppendLine(
$@"	
	partial class {targetClassName}
	{{
		private I{targetClassName}_Bindings{nameSuffix} Bindings{nameSuffix};

		interface I{targetClassName}_Bindings{nameSuffix}
		{{
			void Initialize({targetClassName} targetRoot{dataTypeParam});
			void Update();
			void Cleanup();
			void UpdateSourceOfExplicitTwoWayBindings();
		}}
	}}");

			if (declaringType != null)
			{
				output.AppendLine(
$@"	}}");
			}

			if (targetNamespace != null)
			{
				output.AppendLine(
$@"}}");
			}
		}

		protected virtual void GenerateBindingsExtraFieldDeclarations(StringBuilder output, BindingsData bindingsData)
		{
		}

		protected virtual void GenerateTrackingsExtraFieldDeclarations(StringBuilder output, BindingsData bindingsData)
		{
		}

		protected virtual void GenerateSetDependencyPropertyChangedCallback(StringBuilder output, TwoWayEventData ev, string targetExpr)
		{
		}

		protected virtual void GenerateUnsetDependencyPropertyChangedCallback(StringBuilder output, TwoWayEventData ev, string targetExpr)
		{
		}

		protected virtual void GenerateRegisterDependencyPropertyChangeEvent(StringBuilder output, NotifyPropertyChangedData notifyGroup, NotifyPropertyChangedProperty notifyProp, string cacheVar, string methodName)
		{
		}

		protected virtual void GenerateUnregisterDependencyPropertyChangeEvent(StringBuilder output, NotifyPropertyChangedData notifyGroup, NotifyPropertyChangedProperty notifyProp, string cacheVar, string methodName)
		{
		}

		protected virtual void GenerateDependencyPropertyChangedCallback(StringBuilder output, string methodName, string? a = null)
		{
		}

		protected virtual string GenerateConvertBackCall(Expression converter, string value, string targetType, string parameter, string? cast)
		{
			return $"{cast}{converter}.ConvertBack({value}, typeof(global::{targetType}), {parameter}, null)";
		}
	}
}
