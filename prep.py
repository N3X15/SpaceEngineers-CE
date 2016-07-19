import logging
import os
import shutil
import sys

from lxml import etree

# CONFIGURATION

# Guess.
SE_ROOT_DIR = os.path.abspath(sys.argv[1])

Configurations = ('Release', 'Debug')
#Platforms = ('x86', 'x64')
Platforms = ('x64',)
Libraries = {
    'HavokWrapper_SE': {
        #'x86': [
        #    'Bin/HavokWrapper.dll',
        #    'Bin/HavokWrapper.xml',
        #],
        'x64': [
            'Bin64/HavokWrapper.dll',
            'Bin64/HavokWrapper.xml',
        ],
    },
    'VRage.Native': {
        #'x86': [
        #    'Bin/VRage.Native.dll'
        #],
        'x64': [
            'Bin64/VRage.Native.dll'
        ],
    },
    # 'RakNet': {
    #    'x86': ['Bin/RakNet.dll'],
    #    'x64': ['Bin64/RakNet.dll'],
    # },
    'SteamSDK': {
        #'x86': [
        #    'Bin/SteamSDK.dll',
        #    'Bin/steam_api.dll',
        #],
        'x64': [
            'Bin64/SteamSDK.dll',
            'Bin64/steam_api.dll',
            'Bin64/steam_api64.dll',
        ],
    },
    'SharpDX': {
        'x64': [
            'Bin64/SharpDX.D3DCompiler.dll',
            'Bin64/SharpDX.Desktop.dll',
            'Bin64/SharpDX.Direct2D1.dll',
            'Bin64/SharpDX.Direct3D11.dll',
            'Bin64/SharpDX.Direct3D9.dll',
            'Bin64/SharpDX.DirectInput.dll',
            'Bin64/SharpDX.dll',
            'Bin64/SharpDX.DXGI.dll',
            'Bin64/SharpDX.Mathematics.dll',
            'Bin64/SharpDX.Toolkit.dll',
            'Bin64/SharpDX.XAudio2.dll',
        ]
    }
}


class Reference:

  def __init__(self):
    self.name = ''
    self.hintpath = ''


class VS2015Project:

  def __init__(self):
    self.project = None
    self.references = {}
    self.projectrefs = {}
    self.reference_group = None
    self.projectref_group = None

    self.MSBNS = '{http://schemas.microsoft.com/developer/msbuild/2003}'
    self.NSMAP = {None: 'http://schemas.microsoft.com/developer/msbuild/2003'}
    self.XPATH_NSMAP = {'x': 'http://schemas.microsoft.com/developer/msbuild/2003'}

  def LoadFromFile(self, filename):
    parser = etree.XMLParser(remove_blank_text=True)
    self.project = etree.parse(filename, parser)
    self.references = {}
    self.projectrefs = {}
    for reference in self.project.xpath('//x:Reference', namespaces=self.XPATH_NSMAP):
      refID = reference.get('Include').split(',')[0]
      self.references[refID] = reference
      self.reference_group = reference.getparent()
    for reference in self.project.xpath('//x:ProjectReference', namespaces=self.XPATH_NSMAP):
      refID = reference.find(self.MSBNS + 'Name').text
      self.projectrefs[refID] = reference
      self.projectref_group = reference.getparent()

  def SaveToFile(self, filename):
    with open(filename, 'w') as f:
      f.write(etree.tostring(self.project, pretty_print=True, xml_declaration=True).replace('/>',' />'))

  def subelement(self, parent, name):
    return etree.SubElement(parent, self.MSBNS + name)

  def HasReference(self, refid):
    return refid in self.references or refid in self.projectrefs

  def AddFileRef(self, refID, hintpath, **kwargs):
    if kwargs.get('verbose', False):
      logging.info('Adding file reference %s.', refID)
    if 'verbose' in kwargs:
      del kwargs['verbose']
    reference = self.subelement(self.reference_group, 'Reference')
    reference.attrib['Include'] = refID

    kwargs['HintPath'] = hintpath

    for elementName, elementValue in kwargs.iteritems():
      self.subelement(reference, elementName).text = str(elementValue)

  def RemoveRef(self, refID, verbose=False):
    if refID in self.references:
      if verbose:
        logging.info('Removed file reference %s.', self.references[refID].get('Include'))
      self.reference_group.remove(self.references[refID])
      del self.references[refID]
    if refID in self.projectrefs:
      if verbose:
        logging.info('Removed project reference %s.', self.projectrefs[refID].find(self.MSBNS+'Name').text)
      self.projectref_group.remove(self.projectrefs[refID])
      del self.projectrefs[refID]


AddRefs = []
if __name__ == '__main__':
  logging.basicConfig(
      format='%(asctime)s [%(levelname)-8s]: %(message)s',
      datefmt='%m/%d/%Y %I:%M:%S %p',
      level=logging.DEBUG
  )

  if not os.path.isdir(sys.argv[1]):
    logging.error('{0} is not a directory.', sys.args[1])
    logging.info('USAGE: python prep.py <path\\to\\SpaceEngineers>')
    sys.exit(1)

  newrefs = [
      'HavokWrapper',
      'SharpDX.DirectInput',
      'SharpDX.Toolkit',
      'SteamSDK',
      'VRage.Native',
      #'VRage.OpenVRWrapper',
      #'VRage.Scripting',
  ]
  for project_name in os.listdir('Sources'):
    logging.info('Fixing %s...', project_name)
    project_dir = os.path.join('Sources', project_name)
    for filename in os.listdir(project_dir):
      filename = os.path.join(project_dir, filename)
      if filename.endswith('.csproj'):
        if os.path.isfile(filename + '.user'):
          os.remove(filename + '.user')
        if os.path.isfile(filename + '.new'):
          os.remove(filename + '.new')
        userf = os.path.join(project_dir, project_name + '.user')
        if os.path.isfile(userf):
          os.remove(userf)

        project = VS2015Project()
        project.LoadFromFile(filename)
        for refID in newrefs:
          if project.HasReference(refID):
            project.RemoveRef(refID, verbose=True)
            project.AddFileRef(refID, os.path.join(SE_ROOT_DIR, 'Bin64', refID + '.dll'), verbose=True)
        project.SaveToFile(filename)
        break
