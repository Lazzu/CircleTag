# CircleTag
A library to generate and read circular tags. Designed to transfer small amounts of data from a phone to another via phone screen and camera. An example use-case could be connecting in-game friends by displaying a tag on one phone and reading it with another.
## Project state
Prototype version. I am currently improving the reader to detect the input more easily.
## Usage
Put files in your project, and they are ready to use. They do not depend on any special libraries.
```cs
byte[] data = Encoding.UTF8.GetBytes("ID#1234");
Generator.Settings settings = new Generator.Settings() {
    Width = 512,
    Height = 512
};
byte[] image = Generator.From(data, settings);
byte[] result = Reader.Read(image, width, height);
Console.WriteLine(Encoding.UTF8.GetString(result));
```
## Unity Webcam Reading Example
```cs
public class UnityTagReaderExample : MonoBehaviour
{
    private WebCamTexture _texture;
    private Texture2D _generatedTexture;
    private Color32[] _pixels;
    private byte[] _bytes;
    
    [SerializeField]
    private RawImage _outputImage;

    private void Start()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        _texture = new WebCamTexture(devices[0].name);
        _outputImage.texture = _texture;
    }

    private void Update()
    {
        if(_pixels == null)
        {
            _pixels = new Color32[_texture.width * _texture.height];
            _bytes = new byte[_pixels.length * 4];
        }
        _texture.GetPixels32(_pixels);

        for (int i = 0; i < _pixels.Length; i++)
        {
            Color32 pixel = _pixels[i];
            _bytes[i * 4 + 0] = pixel.r;
            _bytes[i * 4 + 1] = pixel.g;
            _bytes[i * 4 + 2] = pixel.b;
            _bytes[i * 4 + 3] = pixel.a;
        }

        byte[] result = CodeReader.Read(_bytes, _texture.width, _texture.height);
        Debug.Log(Encoding.UTF8.GetString(result));
    }
}
```