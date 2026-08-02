using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models
{
    public class CombineStacksOpenContainersIgnoreEntry : SetPropertyNotifyChanged
    {
        private int _cliloc = -1;
        private int _hue;
        private int _id;

        public int Cliloc
        {
            get => _cliloc;
            set => SetProperty( ref _cliloc, value );
        }

        public int Hue
        {
            get => _hue;
            set => SetProperty( ref _hue, value );
        }

        public int ID
        {
            get => _id;
            set => SetProperty( ref _id, value );
        }
    }
}
