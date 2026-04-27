window.erisStudentKey = {
  animals: [
    "aardvark","albatross","alligator","alpaca","antelope","armadillo","axolotl","badger","bandicoot","barnowl",
    "barracuda","beaver","bison","blackbird","bluejay","boar","bobcat","bonobo","buffalo","bulldog",
    "camel","capybara","caracal","cardinal","cassowary","catfish","chameleon","cheetah","chinchilla","chipmunk",
    "cobra","condor","cougar","coyote","crab","crane","crocodile","crow","deer","dingo",
    "dolphin","donkey","dragonfly","duck","eagle","echidna","egret","elephant","elk","falcon",
    "ferret","finch","flamingo","fox","frog","gazelle","gecko","gerbil","giraffe","gnu",
    "goose","gorilla","goshawk","hamster","hare","hedgehog","heron","hippo","hornet","hummingbird",
    "hyena","ibex","iguana","jackal","jaguar","kangaroo","kingfisher","koala","lemur","leopard",
    "lion","lynx","macaw","magpie","manatee","meerkat","mink","moose","narwhal","newt",
    "ocelot","octopus","opossum","orangutan","orca","osprey","otter","owl","panda","panther"
  ],
  getOrCreate: function () {
    const keyName = "eris-student-key";
    let value = window.localStorage.getItem(keyName);
    if (!value) {
      const idx = Math.floor(Math.random() * window.erisStudentKey.animals.length);
      value = window.erisStudentKey.animals[idx];
      window.localStorage.setItem(keyName, value);
    }
    return value;
  },
  set: function (value) {
    if (value) {
      window.localStorage.setItem("eris-student-key", value);
    }
  }
  ,
  getStickyUrl: function () {
    return window.localStorage.getItem("eris-destination-url");
  },
  setStickyUrl: function (value) {
    if (value !== null && value !== undefined) {
      window.localStorage.setItem("eris-destination-url", value);
    }
  }
};
