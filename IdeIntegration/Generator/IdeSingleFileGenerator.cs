﻿using System;
using System.IO;
using System.Linq;
using TechTalk.SpecFlow.Generator;
using TechTalk.SpecFlow.Generator.Interfaces;
using TechTalk.SpecFlow.Utils;

namespace TechTalk.SpecFlow.IdeIntegration.Generator
{
    public class IdeSingleFileGenerator
    {
        private readonly ProjectInfo _projectInfo;

        public IdeSingleFileGenerator(ProjectInfo projectInfo)
        {
            _projectInfo = projectInfo;
        }

        public event Action<TestGenerationError> GenerationError;
        public event Action<Exception> OtherError;

        public string GenerateFile(string inputFilePath, string outputFilePath, Func<GeneratorServices> generatorServicesProvider,
            Func<string, string> inputFileContentProvider = null, Action<string, string> outputFileContentWriter = null)
        {
            outputFileContentWriter = outputFileContentWriter ?? File.WriteAllText;
            inputFileContentProvider = inputFileContentProvider ?? File.ReadAllText;

            GeneratorServices generatorServices;
            ProjectSettings projectSettings;
            CodeDomHelper codeDomHelper;

            try
            {
                generatorServices = generatorServicesProvider();
                projectSettings = generatorServices.GetProjectSettings();
                codeDomHelper = GenerationTargetLanguage.CreateCodeDomHelper(projectSettings.ProjectPlatformSettings.Language);

                if (outputFilePath == null)
                {
                    outputFilePath = inputFilePath + GenerationTargetLanguage.GetExtension(projectSettings.ProjectPlatformSettings.Language);
                }

                if (_projectInfo.ReferencedSpecFlowVersion == null)
                {
                    return WriteNoSpecFlowVersionReferencedError(outputFilePath, outputFileContentWriter, codeDomHelper);
                }

                var generatorVersion = generatorServices.GetGeneratorVersion();

                if (generatorVersion.Major != _projectInfo.ReferencedSpecFlowVersion.Major
                    || generatorVersion.Minor != _projectInfo.ReferencedSpecFlowVersion.Minor)
                {
                    return WriteSpecFlowVersionConflictError(outputFilePath, outputFileContentWriter, generatorVersion, codeDomHelper);
                }
            }
            catch (Exception ex)
            {
                OnOtherError(ex);
                return null;
            }

            string inputFileContent;
            try
            {
                inputFileContent = inputFileContentProvider(inputFilePath);
            }
            catch (Exception ex)
            {
                OnOtherError(ex);
                return null;
            }

            string outputFileContent = Generate(inputFilePath, inputFileContent, generatorServices, codeDomHelper, projectSettings);

            try
            {
                outputFileContentWriter(outputFilePath, outputFileContent);

                return outputFilePath;
            }
            catch (Exception ex)
            {
                OnOtherError(ex);
                return null;
            }
        }

        private string WriteSpecFlowVersionConflictError(string outputFilePath, Action<string, string> outputFileContentWriter, Version generatorVersion, CodeDomHelper codeDomHelper)
        {
            string errorMessage =
                $@"Version conflict - SpecFlow Visual Studio extension attempted to use SpecFlow code-behind generator {generatorVersion.ToString(2)}, but project '{_projectInfo.ProjectName}' references SpecFlow {_projectInfo.ReferencedSpecFlowVersion.ToString(2)}.
We recommend migrating to MSBuild code-behind generation to resolve this issue.
For more information see https://specflow.org/documentation/Generate-Tests-from-MsBuild/";

            WriteErrorMessageToFile(outputFilePath, outputFileContentWriter, codeDomHelper, errorMessage);
            return outputFilePath;
        }

        private string WriteNoSpecFlowVersionReferencedError(string outputFilePath, Action<string, string> outputFileContentWriter, CodeDomHelper codeDomHelper)
        {
            string errorMessage = $@"Could not find a reference to SpecFlow in project '{_projectInfo.ProjectName}'.
Please add the 'TechTalk.SpecFlow' package to the project and use MSBuild generation instead of using SpecFlowSingleFileGenerator.
For more information see https://specflow.org/documentation/Generate-Tests-from-MsBuild/";

            WriteErrorMessageToFile(outputFilePath, outputFileContentWriter, codeDomHelper, errorMessage);
            return outputFilePath;
        }

        private void WriteErrorMessageToFile(string outputFilePath, Action<string, string> outputFileContentWriter, CodeDomHelper codeDomHelper, string errorMessage)
        {
            var exception = new InvalidOperationException(errorMessage);
            string errorText = GenerateError(exception, codeDomHelper);
            outputFileContentWriter(outputFilePath, errorText);
        }

        private string Generate(string inputFilePath, string inputFileContent, GeneratorServices generatorServices, CodeDomHelper codeDomHelper,
            ProjectSettings projectSettings)
        {
            string outputFileContent;
            try
            {
                var generationResult = GenerateCode(inputFilePath, inputFileContent, generatorServices, projectSettings);

                if (generationResult.Success)
                    outputFileContent = generationResult.GeneratedTestCode;
                else
                    outputFileContent = GenerateError(generationResult, codeDomHelper);
            }
            catch (Exception ex)
            {
                outputFileContent = GenerateError(ex, codeDomHelper);
            }
            return outputFileContent;
        }

        private TestGeneratorResult GenerateCode(string inputFilePath, string inputFileContent, GeneratorServices generatorServices,
            ProjectSettings projectSettings)
        {
            using (var testGenerator = generatorServices.CreateTestGenerator())
            {
                string fullPath = Path.GetFullPath(Path.Combine(projectSettings.ProjectFolder, inputFilePath));
                var featureFileInput =
                    new FeatureFileInput(FileSystemHelper.GetRelativePath(fullPath, projectSettings.ProjectFolder))
                    {
                        FeatureFileContent = inputFileContent
                    };
                return testGenerator.GenerateTestFile(featureFileInput, new GenerationSettings());
            }
        }

        private string GenerateError(TestGeneratorResult generationResult, CodeDomHelper codeDomHelper)
        {
            var errorsArray = generationResult.Errors.ToArray();

            foreach (var testGenerationError in errorsArray)
            {
                OnGenerationError(testGenerationError);
            }

            return string.Join(Environment.NewLine, errorsArray.Select(e => codeDomHelper.GetErrorStatementString(e.Message)).ToArray());
        }

        private string GenerateError(Exception ex, CodeDomHelper codeDomHelper)
        {
            var testGenerationError = new TestGenerationError(ex);
            OnGenerationError(testGenerationError);

            string exceptionText =  ex.Message + Environment.NewLine +
                                              Environment.NewLine +
                                ex.Source + Environment.NewLine + 
                                ex.StackTrace;

            string errorMessage = string.Join(Environment.NewLine, exceptionText
                .Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                .Select(codeDomHelper.GetErrorStatementString));

            return errorMessage;
        }

        protected virtual void OnGenerationError(TestGenerationError testGenerationError)
        {
            if (GenerationError != null)
                GenerationError(testGenerationError);
        }

        protected virtual void OnOtherError(Exception exception)
        {
            if (OtherError != null)
                OtherError(exception);
        }
    }
}